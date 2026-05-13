# TTS Pipeline Analysis — Resilience & Status Correctness

**Date**: 2026-05-13  
**Branch**: `analysis/tts-pipeline-resilience`  
**Scope**: Full pipeline from `AppConfig` → `TtsService` init → `AudioResponseHandler` → `TtsContentFilter`/`TtsSummarizer` → provider `SynthesizeAsync` → `AudioPlayerService.Play`

---

## Pipeline Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                        CONFIGURATION                                │
│  AppConfig.cs                                                       │
│  ├─ TtsProvider, TtsOutputMode (siso/always-on/off)                 │
│  ├─ TtsDirectMaxChars, TtsMaxChars, TtsCodeBlockMode                │
│  ├─ TtsTooLongFallback, TtsUseDirectLlmSummary                      │
│  ├─ TtsOpenAiApiKey, TtsSubscriptionKey, TtsRegion, TtsVoice        │
│  └─ Coqui/Piper/Python paths                                        │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────────┐
│                   INITIALIZATION (parallel)                         │
│  AppRunner.InitializeTtsProviderAsync()                              │
│  ├─ factory.CreateTtsService(cfg) → new TtsService(config, console) │
│  │   └─ Switch on TtsProviderType → creates ITextToSpeech impl      │
│  ├─ ttsService.ReleaseProvider() → transfers ownership              │
│  └─ Status: Green on success / Red on failure                       │
│                                                                     │
│  GatewayService.WireTtsOnProviderReadyAsync()                       │
│  ├─ Awaits ttsInitTask                                              │
│  ├─ Creates AudioResponseHandler with provider, player, summarizer  │
│  └─ coordinator.SetAudioHandler(handler)                            │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────────┐
│                     OUTPUT COORDINATION                             │
│  AgentOutputCoordinator                                             │
│  ├─ OnAgentReplyFull(body)     ──→ handler.HandleAudioMarkerAsync()│
│  └─ OnAgentReplyDeltaEnd()     ──→ handler.HandleAudioMarkerAsync()│
│                                    (using AccumulatedText)          │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────────┐
│                     AUDIO RESPONSE HANDLING                         │
│  AudioResponseHandler.PlayTtsAsync(text)                            │
│  ├─ CanPlayTts() checks:                                           │
│  │   ├─ text not empty                                              │
│  │   ├─ TtsMaxChars > 0, TtsDirectMaxChars > 0                     │
│  │   ├─ TtsOutputMode: always-on / siso (voice input?) / off       │
│  │   ├─ SISO checks: LastInputWasVoice, agent match                │
│  │   ├─ DuringReplay suppression                                    │
│  │   └─ ttsProvider != null                                        │
│  ├─ PrepareTextForTtsAsync():                                       │
│  │   ├─ If special formatting + LLM enabled → TtsSummarizer        │
│  │   └─ Else: TtsContentFilter.SanitizeForTts() + truncate         │
│  └─ SynthesizeAndPlay():                                           │
│      └─ BackgroundJobRunner.RunAndForget(async () =>                │
│          ├─ provider.SynthesizeAsync(text, voice, null, None)       │
│          └─ audioPlayer.Play(audioBytes))                           │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────────┐
│                     PROVIDER SYNTHESIS                              │
│  ITextToSpeech.SynthesizeAsync(text, voice, model, ct)              │
│  ├─ EdgeTtsProvider:    Azure REST API (SSML)                       │
│  ├─ OpenAiTtsProvider:  OpenAI TTS API                              │
│  ├─ CoquiUvTtsProvider: Local Coqui via uv (auto-download)          │
│  ├─ PiperTtsProvider:   Local Piper binary                          │
│  └─ PythonTtsProvider:  Legacy Python/Coqui subprocess              │
└──────────────────────────┬──────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────────┐
│                     AUDIO PLAYBACK                                  │
│  AudioPlayerService.Play(byte[])                                    │
│  ├─ Stop() current playback                                        │
│  ├─ Try WaveFileReader → PlayInternal(waveStream)                   │
│  ├─ Catch → RawSourceWaveStream (16kHz/16-bit/mono fallback)       │
│  └─ NAudio WaveOutEvent.Play()                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Status Update Flow

```
┌───────────────────┐
│   TTS Status Dot  │  "TTS:●" (green) / "TTS:·" (yellow animating) / "TTS:●" (red)
└───────────────────┘

Status transitions:
  [nothing] ──(init success)──→ Green
  [nothing] ──(init failure)──→ Red
  Green ──(reconfig start)──→ Yellow ──(reconfig success)──→ Green
  Green ──(reconfig start)──→ Yellow ──(reconfig failure)──→ Red
  Red   ──(reconfig start)──→ Yellow ──(reconfig success)──→ Green
  Red   ──(reconfig start)──→ Yellow ──(reconfig failure)──→ Red

Animation: Yellow dot cycles through [·, •, ●, •] every 600ms
```

---

## Resilience Analysis — Findings

### 1. ⚠️ CRITICAL: No runtime synthesis error handling affects status

**Location**: `AudioResponseHandler.SynthesizeAndPlay()` + `BackgroundJobRunner.RunAndForget()`

Synthesis runs in a fire-and-forget task. If `SynthesizeAsync()` fails:
- The error is logged to `BackgroundJobRunner` history (internal only)
- `_console.PrintWarning()` is called inside the job
- **TTS status dot stays Green** — no status degradation
- **User has no indication TTS is broken** beyond a single console warning

The status dot only reflects initialization/reconfiguration success, not runtime health.

```csharp
// AudioResponseHandler.SynthesizeAndPlay() — current code
_jobRunner.RunAndForget(async () =>
{
    var audioBytes = await _ttsProvider!.SynthesizeAsync(
        textToSpeak, _config.TtsVoice, null, CancellationToken.None);
    // 🔴 No catch block, no status update on failure
    if (audioBytes != null && audioBytes.Length > 0)
        _audioPlayer.Play(audioBytes);
    else
        _console.PrintWarning("TTS synthesis returned null/empty audio.");
}, $"tts-synthesis-{preview}");
```

### 2. ⚠️ CRITICAL: No retry logic for transient synthesis failures

Neither `AudioResponseHandler` nor any provider implements retry. A transient network blip to Azure/OpenAI API will silently fail. The `CancellationToken.None` means even app shutdown can't interrupt in-progress synthesis.

### 3. ⚠️ MEDIUM: No Yellow status during initial TTS startup

**Location**: `AppRunner.InitializeTtsProviderAsync()`

During initial app startup, TTS goes from uninitialized to Green/Red instantly. Unlike STT (which explicitly sets Yellow before background verification), TTS init shows no transitional animation. The user sees nothing while a slow Coqui model download might take 10+ seconds.

```csharp
// InitializeTtsProviderAsync — should set Yellow before starting:
// 🔴 Missing: _statusService.SetServiceStatus(ServiceKind.Tts, StatusColor.Yellow);
_console.Log("tts", "Initializing TTS...");
using var ttsService = _factory.CreateTtsService(cfg, _console);
```

### 4. ⚠️ MEDIUM: TtsService constructor blocks on PythonTtsProvider init

```csharp
// TtsService constructor
if (_provider is Providers.PythonTtsProvider pythonProvider)
{
    pythonProvider.InitializeAsync(_cts.Token).GetAwaiter().GetResult();
    // 🔴 Blocks the calling thread; CoquiUv may download models here
}
```

The CoquiUv provider may download models during initialization — if called from a UI thread this would freeze. Currently it runs on a background task (`Task.Run`), so it's safe, but the design is fragile.

### 5. ⚠️ MEDIUM: AudioPlayerService has race condition on Stop/Play

```csharp
// AudioPlayerService.Play()
Stop(); // Stop current playback — but Stop() may throw
// If Stop() threw, we'd continue with stale state
// 🔴 No try/catch around Stop()
```

If `CleanupPlayback()` throws during `Stop()`, the `_waveOut` and `_activeStream` may not be fully released before starting new playback.

### 6. ⚠️ LOW: Edge provider with null key creates confusing state

```csharp
// TtsService constructor
TtsProviderType.Edge => config.TtsSubscriptionKey != null
    ? new Providers.EdgeTtsProvider(...)
    : null,  // 🔴 Silent null — IsConfigured = false, status stays previous
```

If the user switches to Edge without a key, `IsConfigured = false`, TTS silently stops working, and the status dot stays Green (since no reconfig error occurred).

### 7. ⚠️ LOW: `HasSpecialFormatting` scans for `#` — false positive for hashtags

```csharp
// TtsContentFilter.HasSpecialFormatting
return text.Contains("```") || text.Contains("`") || text.Contains("**") ||
       text.Contains("#") || text.Contains("|") || text.Contains("http");
// 🔴 '#' matches both markdown headings AND plain hashtags like #topic
```

This could trigger unnecessary LLM summarization for messages containing just a hashtag.

---

## Status Correctness Analysis — Findings

### 8. ⚠️ STATUS: TTS dot shows Green when synthesis is failing

The TTS status dot in the separator bar only reflects initialization/reconfiguration results. Once Green, it stays Green regardless of runtime synthesis failures. There is no health-check loop or failure counter that could degrade the status.

**Impact**: User sees "TTS:●" (green) but gets no audio output. Console warnings are easy to miss in busy output.

### 9. ⚠️ STATUS: AudioResponseHandler doesn't communicate synthesis failures back

No callback/delegate from `AudioResponseHandler` to `StatusService` or `AppRunner`. When synthesis fails in the background job, there's no path to update the status dot.

### 10. ✅ GOOD: Reconfig status transitions are correct

`HandleTtsConfigChanged` properly sets Yellow → attempts reconfig → Green/Red.

### 11. ✅ GOOD: StatusAnimationManager is well-isolated

Timer lifecycle, frame advancement, and Yellow detection are all correctly managed. No leaks, no runaway timers.

### 12. ✅ GOOD: Dispose chains are complete

`AppRunner` → `GatewayService.Dispose` → `AgentOutputCoordinator.Dispose` → `AudioResponseHandler.Dispose` → `AudioPlayerService.Dispose` → `BackgroundJobRunner.Dispose`. Each level catches its own exceptions.

---

## Summary Matrix

| # | Severity | Category | Issue |
|---|----------|----------|-------|
| 1 | CRITICAL | Resilience | No runtime synthesis error handling — TTS status stays Green when synthesis fails |
| 2 | CRITICAL | Resilience | No retry for transient synthesis failures; CancellationToken.None |
| 3 | MEDIUM | Status | No Yellow status during initial TTS startup (unlike STT) |
| 4 | MEDIUM | Resilience | `.GetAwaiter().GetResult()` blocks on PythonTtsProvider init |
| 5 | MEDIUM | Resilience | AudioPlayerService race condition between Stop() and Play() |
| 6 | LOW | Status | Edge with null key: IsConfigured=false, status unchanged (stale) |
| 7 | LOW | Content | `HasSpecialFormatting` false positive for `#` (plain hashtags) |
| 8 | STATUS | Status | No health monitoring — Green dot never degrades at runtime |
| 9 | STATUS | Status | AudioResponseHandler has no communication path back to StatusService |

---

## Recommendations

### Priority 1 — Runtime Resilience
1. **Add synthesis failure handling to `AudioResponseHandler`**: Wrap `SynthesizeAsync` in try/catch. On failure, invoke a callback/delegate to degrade TTS status to Red (or Yellow for transient). Consider a failure counter with auto-reset.
2. **Pass proper CancellationToken**: Replace `CancellationToken.None` with a token from a CTS that's cancelled on shutdown/dispose.
3. **Add retry logic**: At minimum, retry once for HTTP-based providers (Edge, OpenAI) on transient errors (5xx, timeout).

### Priority 2 — Status Completeness
4. **Set Yellow during initial TTS init**: Add `_statusService.SetServiceStatus(ServiceKind.Tts, StatusColor.Yellow)` at the start of `InitializeTtsProviderAsync()`.
5. **Add status communication path**: Give `AudioResponseHandler` an `Action<StatusColor>?` callback for synthesis health reporting. Wire it from AppRunner/StatusService.

### Priority 3 — Robustness
6. **Guard `AudioPlayerService.Stop()` call in `Play()`**: Wrap in try/catch to prevent state corruption.
7. **Edge null-key handling**: Log a clear warning and set TTS status to Red when Edge provider is selected but key is missing.
8. **Fix `HasSpecialFormatting`**: Exclude `#` from the check, or make it match `#{1,6}\s` (markdown heading pattern only).

---

## File Index

| File | Role |
|------|------|
| `AppConfig.cs` | TTS configuration properties |
| `AppRunner.TtsSetup.cs` | TTS provider initialization, status updates |
| `AppRunner.ConfigSetup.cs` | `HandleTtsConfigChanged` — reconfig handler |
| `AppRunner.cs` | Orchestration: creates TTS init task, wires config handlers |
| `AppRunner.ShellSetup.cs` | StreamShell + hotkey wiring (TTS not wired here) |
| `TtsService.cs` | Provider factory, `ReleaseProvider()`, disposal |
| `ITtsService.cs` | Interface for TTS service |
| `ITextToSpeech.cs` | Provider interface (`SynthesizeAsync`) |
| `AudioResponseHandler.cs` | TTS mode logic, text prep, synthesis dispatch |
| `TtsContentFilter.cs` | Markdown/code/URL sanitization |
| `TtsSummarizer.cs` | Direct LLM summarization for TTS |
| `AudioPlayerService.cs` | NAudio playback (WAV/PCM) |
| `IAudioPlayer.cs` | Audio player interface |
| `BackgroundJobRunner.cs` | Fire-and-forget job execution |
| `AgentOutputCoordinator.cs` | Routes agent events → audio handler |
| `GatewayService.cs` | TTS wiring continuation, `RecreateTtsProviderAsync` |
| `StatusService.cs` | Central status management, animation |
| `ServiceStatusPart.cs` | TTS status dot rendering ("TTS:●") |
| `StatusAnimationManager.cs` | Yellow dot animation timer |
| `StatusColorExtensions.cs` | StatusColor → Spectre color mapping |
| `ServiceKind.cs` | Enum: Gateway/Tts/Stt/DirectLlm |
| `EdgeTtsProvider.cs` | Example provider (Azure TTS) |

---

## Resolution Log (2026-05-13)

All 6 findings resolved on branch `analysis/tts-pipeline-resilience`:

| # | Commit | Resolution |
|---|--------|-----------|
| 1 + 8 + 9 | `09bff92` | Added `Action<bool>?` synthesis status callback from AudioResponseHandler → GatewayService → AppRunner → StatusService. Try/catch around synthesis with true (Green) / false (Red) reporting. |
| 2 | `b54639c` | Added `_synthesisCts` (cancelled on Dispose), replaced `CancellationToken.None`. Retry loop: up to 1 retry with 500ms delay. OCE returns silently (shutdown). |
| 3 | `0f451fb` | `_statusService.SetServiceStatus(ServiceKind.Tts, StatusColor.Yellow)` at start of `InitializeTtsProviderAsync()`. |
| 4 | `081e026` | `Stop()` wrapped in inner try/catch in both Play methods. `CleanupPlayback()` nulls fields before disposal so exceptions don't leave stale state. |
| 5 | `4a8bbbd` | TtsService logs warning when Edge has no key. `RecreateTtsProviderAsync` throws when provider is null → reconfig handler sets Red. More descriptive init logs. |
| 7 (as #6) | `0bbc345` | `HasSpecialFormatting` uses `^#{1,6}\s` regex instead of `Contains("#")`. Eliminates false positives on plain hashtags. |

**Test results**: 877 passed, 0 failed, 3 skipped — unchanged from baseline.

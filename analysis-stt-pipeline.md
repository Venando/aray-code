# STT Pipeline Resilience Analysis

**Date:** 2026-05-13  
**Branch:** `analysis/stt-pipeline-resilience`  
**Scope:** Full pipeline from AppConfig → AudioRecorder → Transcriber → TextMessageSender → Gateway

---

## Pipeline Flow Diagram

```
AppConfig (SttProvider, keys, models, audio params)
    │
    ▼
AudioService.ctor()
    ├── AudioRecorder (NAudio/sox/arecord)
    └── TranscriberFactory.Create(config) → ITranscriber
            ├── GroqTranscriber (default, cloud)
            ├── OpenAiTranscriberAdapter (cloud)
            ├── WhisperCppTranscriberAdapter (local)
            └── FasterWhisperTranscriberAdapter (local)
    │
    ▼
AppRunner: VerifyTranscriberAsync (0.25s silence → validate)
    │  STT Status: Yellow → Green ✅ / Red ❌
    │
    ▼
AppLoop.RunAsync()  [50ms poll loop]
    │
    ├── PollHotkeyState() → PttStateMachine
    │       Idle → Recording (press) → Processing (release)
    │
    ├── HandleRecordingState()
    │       Start:  AudioService.StartRecording()
    │       Stop:   AudioService.StopAndTranscribeAsync(ct)
    │               ├── recorder.StopRecording() → byte[] wav
    │               ├── transcriber.TranscribeAsync(wav, ct) → string
    │               └── return transcribed (or null on error)
    │
    └── SendTranscribedMessage(text, ct)
            └── TextMessageSender.SendAsync() → GatewayService.SendTextAsync() → GatewayClient
```

---

## Resilience Findings

### 🔴 CRITICAL

#### C1. GroqTranscriberAdapter drops CancellationToken

**File:** `GroqTranscriberAdapter.cs:18`  
**Problem:** The adapter receives `ct` but doesn't pass it to `_inner.TranscribeAsync()`:

```csharp
// GroqTranscriberAdapter — ct is received but IGNORED
public async Task<string> TranscribeAsync(byte[] wavBytes, string fileName, CancellationToken ct)
{
    return await _inner.TranscribeAsync(wavBytes, fileName).ConfigureAwait(false);  // ← ct dropped!
}
```

**Root cause:** `GroqTranscriber.TranscribeAsync` signature has no `CancellationToken` parameter at all:

```csharp
// GroqTranscriber — no CancellationToken parameter
public async Task<string> TranscribeAsync(byte[] wavBytes, string fileName)
```

**Impact:** If Groq API hangs (network stall, DNS timeout, etc.), the PTT loop freezes. The user's only option is to kill the app. This is the default STT provider.

**Fix:** Add `CancellationToken` parameter to `GroqTranscriber.TranscribeAsync`, pass it through to `HttpClient.PostAsync` and retry `Task.Delay` calls.

---

#### C2. GroqRetryCount defaults to 0 — zero retries

**File:** `AppConfig.cs:68`  
**Problem:** `GroqRetryCount` defaults to `0`, meaning a single network glitch causes transcription failure:

```csharp
public int GroqRetryCount { get; set; } = 0;           // ← zero retries
public int GroqRetryDelayMs { get; set; } = 1000;       // configured but never used
public double GroqRetryBackoffFactor { get; set; } = 2.0; // configured but never used
```

**Impact:** Any transient network error (DNS blip, proxy hiccup, Groq 503) kills transcription with no retry. The user has to re-record and try again.

**Fix:** Default to `1` or `2`. The retry infrastructure in GroqTranscriber is well-implemented — it just never gets used.

---

### 🟠 HIGH

#### H1. No transcription timeout in AppLoop

**File:** `AppLoop.cs:96-113`  
**Problem:** `HandleRecordingComplete` awaits `StopAndTranscribeAsync(ct)` with no timeout:

```csharp
var transcribed = await _audioService.StopAndTranscribeAsync(ct);  // ← no timeout
```

The `ct` here is the outer loop cancellation token — not a dedicated transcription timeout. If the transcriber hangs (long API response, stuck process), the loop blocks indefinitely.

**Impact:** The entire PTT loop is frozen until the transcriber completes or the user Ctrl+C's. No new recordings, no input handling, no status updates.

**Fix:** Wrap with a linked CTS using a configurable timeout (e.g., 30s for cloud, 120s for local):

```csharp
using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
var transcribed = await _audioService.StopAndTranscribeAsync(linkedCts.Token);
```

---

#### H2. Transcription failure doesn't update STT status

**File:** `AudioService.cs:98-110`, `AppRunner.cs:212-222`  
**Problem:** STT status is set to Green after successful verification, but individual transcription failures never update it:

```csharp
// AudioService.StopAndTranscribeAsync — catches exception, returns null, status stays Green
catch (Exception ex)
{
    _console.PrintError($"Transcription failed ...");
    return null;  // ← No status update!
}
```

**Flow:**
1. Startup: STT Yellow → verification succeeds → Green ✅
2. 10 minutes later: Groq returns 503 → transcription fails → status STILL Green ❌

The user sees a green STT dot, but transcription is actually broken. The only status transitions are:
- AudioService construction/verification (Yellow → Green/Red)
- STT config changes (Yellow → Green)
- Never during actual transcription work

**Fix:** Add transient error tracking. If N consecutive transcriptions fail, degrade to Yellow/Red. Reset on first success.

---

#### H3. VerifyTranscriberAsync uses CancellationToken.None

**File:** `AppRunner.cs:217`  
**Problem:** The verification task uses `CancellationToken.None`, meaning it can't be cancelled when the app shuts down:

```csharp
await audioService.VerifyTranscriberAsync(_cfg, _console, CancellationToken.None);  // ← Can't cancel!
```

If verification is in progress (e.g., waiting on a hung Groq API) when the user hits Ctrl+C, the app must wait for the task to complete or timeout before exiting.

**Impact:** Delayed shutdown. If Groq is unreachable, the app hangs for the HttpClient default timeout (~100s) before exiting.

**Fix:** Pass a cancellation token linked to the app's shutdown token.

---

#### H4. No visual "transcribing" state

**File:** `StatusService.cs`, `ServiceStatusPart.cs`  
**Problem:** Three status colors exist — Green (ok), Yellow (transitioning), Red (error). But Yellow is only set during:
- Startup verification
- Config re-creation

During normal hotkey-triggered transcription (which can take seconds for local whisper), the dot stays Green. There's no indication that work is happening.

**Impact:** User presses hotkey, sees green STT dot, waits silently. If transcription takes 5 seconds (local model), it feels like the app hung.

**Fix:** Add a `Busy`/`Active` state that sets the dot to Yellow during active transcription. Or add a spinning/different animation. This would require `AudioService` to call `SetServiceStatus(Stt, Yellow)` before transcription and restore after.

---

### 🟡 MEDIUM

#### M1. Transcription failure is silent to the user

**File:** `AudioService.cs:109`, `AppLoop.cs:96-98`  
**Problem:** When transcription fails, it returns null, and AppLoop receives null — nothing happens:

```csharp
var transcribed = await _audioService.StopAndTranscribeAsync(ct);
if (transcribed != null) { /* send */ }
// else: silent no-op — user recorded audio, got nothing back
```

The error is logged to the console (`PrintError`), but if the user is in another window, they won't see it. No retry prompt, no "try again" dialog.

**Impact:** User records audio, releases hotkey, sees nothing happen. Unclear if the app detected the recording at all.

**Fix:** Show a visible error indicator. Consider auto-retry for transient errors. Offer "retry recording?" prompt.

---

#### M2. TextMessageSender swallows send errors without retry

**File:** `TextMessageSender.cs:26-33`, `AppLoop.cs:145-152`  
**Problem:** Double error wrapping — both `TextMessageSender` and `AppLoop` catch exceptions, but neither retries:

```csharp
// TextMessageSender — catches, displays, swallows
catch (GatewayException gex) { ShowGatewayError(gex); }
catch (Exception ex) { _console.PrintError($"Send failed: {ex.Message}"); }

// AppLoop — catches, displays, swallows
catch (Exception ex) { _console.LogError("ptt", $"Failed to send: ..."); }
```

If the gateway is in the middle of reconnecting when a transcribed message arrives, the send fails and the transcription is lost. No queue, no retry.

**Impact:** Transcription was successful (STT provider returned text), but the message is lost because the gateway was briefly disconnected.

**Fix:** Queue unsent messages during reconnect. Or at minimum, show the transcribed text so the user can manually re-send.

---

#### M3. GroqTranscriber has no CancellationToken at all

**File:** `GroqTranscriber.cs:83`  
**Problem:** The entire Groq transcription path lacks cancellation support:

```csharp
public async Task<string> TranscribeAsync(byte[] wavBytes, string fileName)  // ← No CancellationToken
{
    // ...
    response = await _http.PostAsync(GroqApiUrl, content).ConfigureAwait(false);  // ← No ct!
    // ...
    await Task.Delay(delay).ConfigureAwait(false);  // ← No ct! Can't cancel retry waits
}
```

This is in addition to C1 — even if the adapter tried to pass ct, the inner class doesn't support it.

**Fix:** Add `CancellationToken` parameter, pass to all `HttpClient.PostAsync` and `Task.Delay` calls.

---

#### M4. "Too short" recording has no retry

**File:** `AudioService.cs:91-95`  
**Problem:** If the WAV is < 1024 bytes, it's silently skipped:

```csharp
if (wav.Length < 1024)
{
    _console.PrintWarning("Too short (<1KB), skipped.");
    return null;  // ← No status update, no retry, recording is lost
}
```

**Impact:** Brief recordings (taps, quick words) are dropped. The warning message is useful but the recording is lost — user must re-record.

**Fix:** Consider auto-retrying the recording if it's too short (show "Recording was too short, try again"). Or lower the threshold.

---

### 🟢 LOW

#### L1. AudioRecorder CLI fallback throws on double failure

**File:** `AudioRecorder.cs:93-106`  
**Problem:** If `sox` fails, it tries `arecord`. If both fail, it throws:

```csharp
_recProc = System.Diagnostics.Process.Start(psi)
           ?? throw new InvalidOperationException(
               "No audio recorder found. Install sox or NAudio (Windows).");
```

**Impact:** App crashes on startup if neither sox nor arecord is installed. Unlikely on most Linux distros (arecord is part of ALSA-utils), but possible on minimal installations.

**Fix:** Catch and provide a clear error message at a higher level, allowing the app to start without audio.

---

#### L2. WhisperCpp binary falls back to bare "whisper"

**File:** `WhisperCppTranscriberAdapter.cs:53-56`  
**Problem:** If binary not found on PATH, falls back to literal string "whisper":

```csharp
return WhisperCppModelManager.FindWhisperBinary() ?? binaryPath ?? "whisper";
```

This will fail at transcription time with a confusing `Process.Start` error.

**Impact:** Delayed error — user gets through config wizard successfully, then transcription fails cryptically.

**Fix:** Validate binary exists at construction time. Throw `TranscriberException` with clear instructions.

---

## Status Update Correctness

### Current state machine:

```
               ┌──────────────┐
       ┌──────►│   Yellow     │◄──────┐
       │       │ (verifying)  │       │
       │       └──────┬───────┘       │
       │              │               │
       │    ┌─────────┴─────────┐     │
       │    │                   │     │
       │    ▼                   ▼     │
       │ ┌──────┐          ┌──────┐   │
       │ │ Green│          │ Red  │   │
       │ │ (ok) │          │(fail)│   │
       │ └──┬───┘          └──────┘   │
       │    │                         │
       │    │  config change          │
       │    └─────────────────────────┘
       │
       └────  transcription fails (NOT IMPLEMENTED)
```

### Gaps:
1. **Transcription failures** (H2) — stay Green, never transition to Red
2. **Active transcription** (H4) — no busy/working indication
3. **Transient failures** — no concept of "degraded" (N failures in a row → Yellow)
4. **Recovery from Red** — Red never transitions back to Green without reconfig (no self-healing)

### What works correctly:
- Config-driven recreation: Yellow → Green ✅
- Gateway disconnect/connect: Green/Red correctly via events ✅
- Animation timer starts/stops correctly when Yellow status appears/disappears ✅
- All three StatusColor values used for STT ✅
- STT status part renders with correct label "STT:" and colored dot ✅

---

## Fix Verification (2026-05-13)

All 12 issues resolved in branch `analysis/stt-pipeline-resilience`:

| Commit | Issues | Changes |
|--------|--------|---------|
| `0c57ddd` | M3 | Added `CancellationToken ct` param to GroqTranscriber.TranscribeAsync — passed to HttpClient, Task.Delay, ReadAsStringAsync |
| `27d470c` | C1 | GroqTranscriberAdapter passes ct through instead of dropping it |
| `35a8a29` | C2 | GroqRetryCount default 0→2 — transient errors get 2 retries by default |
| `36b634f` | H3 | CancellationToken.None → cancellable ct; OCE caught separately from errors |
| `6c3462c` | H1 | TranscriptionTimeoutSeconds=30 config; linked timeout CTS in StopAndTranscribeAsync |
| `7791481` | H2+H4+M1 | TranscriptionStatusCallback — Yellow(Running)/Green(OK)/Red(Fail) lifecycle wired to StatusService |
| `d6b6137` | M2+M4+L1+L2 | Send-failure preserves text; clearer too-short message; recorder start errors caught; whisper binary validated early |

**Test results:** 883 passed, 3 skipped, 0 failed ✅

---

## Summary

| # | Severity | Issue | Component |
|---|----------|-------|-----------|
| C1 | Critical | CancellationToken dropped in GroqTranscriberAdapter | GroqTranscriberAdapter |
| C2 | Critical | GroqRetryCount defaults to 0 (no retries) | AppConfig |
| H1 | High | No transcription timeout in AppLoop | AppLoop |
| H2 | High | Transcription failure doesn't update STT status | AudioService + StatusService |
| H3 | High | VerifyTranscriberAsync uses CancellationToken.None | AppRunner |
| H4 | High | No visual "transcribing" state | AudioService + StatusService |
| M1 | Medium | Transcription failure is silent to user | AudioService + AppLoop |
| M2 | Medium | Send errors swallowed without retry or queue | TextMessageSender |
| M3 | Medium | GroqTranscriber has no CancellationToken at all | GroqTranscriber |
| M4 | Medium | "Too short" recording has no retry/prompt | AudioService |
| L1 | Low | AudioRecorder CLI throws on double fallback failure | AudioRecorder |
| L2 | Low | WhisperCpp binary fallback fails at runtime | WhisperCppTranscriberAdapter |

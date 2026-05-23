# Analysis: Adding Supertonic 3 as a TTS Provider

**Date:** 2026-05-23  
**Branch:** `feat/analyze-supertonic-tts`  
**Project:** openclaw-ptt-client

---

## 1. What is Supertonic 3?

Supertonic 3 is an **open-weight, on-device TTS model** by Supertone Inc.

| Spec | Value |
|------|-------|
| Parameters | ~99M |
| Runtime | ONNX Runtime (CPU-only capable) |
| Audio | 44.1kHz 16-bit WAV |
| Languages | 31 (including Ukrainian, Japanese, Arabic) |
| Preset voices | 10 (M1–M5, F1–F5) |
| Zero-shot voice cloning | Via Voice Builder (custom voice JSON) |
| Expression tags | `<laugh>`, `<whisper>`, `<cry>` etc. |
| License | MIT (code), OpenRAIL-M (model) |
| Speed | Real-time on CPU, ~900–1300 chars/s on M4 Pro |

**Key selling points vs. existing providers:**
- **Faster than Coqui** (which is a heavy TTS framework)
- **Open weights** unlike Edge/OpenAI (cloud-dependent, paid)
- **No GPU required** (unlike Coqui which benefits from CUDA)
- **31 languages** vs. Piper's ~10–15
- **Voice cloning** out of the box
- **Native C# ONNX bindings** possible (not just Python)

---

## 2. Existing TTS Provider Architecture

The app has a clean **provider pattern**:

```
ITextToSpeech (interface)
├── OpenAiTtsProvider   — HTTP to OpenAI API
├── EdgeTtsProvider     — HTTP to Azure TTS
├── CoquiUvTtsProvider  — Python subprocess via uv
├── PiperTtsProvider    — Native process (piper binary)
└── (Your new one) SupertonicTtsProvider
```

**Integration points to touch:**
1. `TtsProviderType` enum — add `Supertonic`
2. `ITextToSpeech` interface — implement
3. `TtsService.CreateProvider()` — add switch case
4. `AppConfig` — add config fields
5. `TtsConfigSection` — add wizard flow
6. `AppConfig.cs` description dictionary — add entries

---

## 3. Integration Approaches (Ranked)

### Approach A: Python SDK via uv (RECOMMENDED ✅)

**Pattern:** Same as `CoquiUvTtsProvider` — use `uv run` to invoke a Python child process with a JSON stdin/stdout protocol.

**Implementation:**
```python
# supertonic_service.py (bundled in the app)
from supertonic import TTS
import sys, json, base64, io
import soundfile as sf

tts = TTS(auto_download=True)

for line in sys.stdin:
    req = json.loads(line)
    style = tts.get_voice_style(voice_name=req.get("voice", "M1"))
    wav, duration = tts.synthesize(
        text=req["text"],
        lang=req.get("lang", "en"),
        voice_style=style,
        total_steps=req.get("quality", 8),
        speed=req.get("speed", 1.05),
    )
    # Return WAV bytes as base64
    buf = io.BytesIO()
    sf.write(buf, wav.squeeze(), 44100, format="WAV")
    result = {"id": req["id"], "wav": base64.b64encode(buf.getvalue()).decode()}
    sys.stdout.write(json.dumps(result) + "\n")
    sys.stdout.flush()
```

**C# side:** `SupertonicTtsProvider` mirrors `CoquiUvTtsProvider`:
- `uv run --with supertonic supertonic_service.py`
- JSON line-protocol for synthesis requests
- Base64-encoded WAV response
- Or: return file path like Coqui does

**Pros:**
- ✅ Reuses existing `uv` infrastructure (CoquiUv already has this pattern)
- ✅ No new dependency management — `uv run` auto-installs `supertonic`
- ✅ `CoquiUvTtsProvider` is a direct blueprint — copy, adapt, done
- ✅ Works on any platform where Python + uv run
- ✅ No HTTP server to manage
- ✅ Zero-shot voice cloning works (Voice Builder JSON)

**Cons:**
- ❌ Adds latency per-request (Python process start/teardown, but Coqui keeps it running)
- ❌ uv + Python dependency (same as Coqui — acceptable)
- ❌ WAV bytes over stdout is bulkier than file path

**Effort:** ~2–3 hours (clone CoquiUvTtsProvider, swap Python script)

---

### Approach B: Local HTTP Server (supertonic serve)

**Pattern:** Use `supertonic serve` as a background HTTP server, then call it like OpenAiTtsProvider calls OpenAI.

**Implementation:**
```
# Start server (in background process)
uv run --with supertonic[serve] supertonic serve --host 127.0.0.1 --port 7788

# C# client -> POST /v1/audio/speech (OpenAI-compatible!)
```

**C# side:** Can literally reuse most of `OpenAiTtsProvider`! The `/v1/audio/speech` endpoint is OpenAI-compatible:
```csharp
public sealed class SupertonicTtsProvider : ITextToSpeech, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly Process? _serverProcess;

    public async Task<byte[]> SynthesizeAsync(string text, string? voice = null, ...)
    {
        var request = new
        {
            model = "supertonic-3",
            input = text,
            voice = voice ?? "M1",
            response_format = "wav",
        };
        var response = await _http.PostAsJsonAsync(
            $"{_baseUrl}/v1/audio/speech", request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }
}
```

**Pros:**
- ✅ OpenAI-compatible endpoint — minimal code, reuses patterns
- ✅ Server stays hot between requests (no per-call startup cost)
- ✅ `/v1/health` endpoint for readiness checks
- ✅ Voice Builder JSON import via `/v1/styles/import`
- ✅ Batch endpoint for multiple synthesis
- ✅ Works from any language, not just Python

**Cons:**
- ❌ Must manage server lifecycle (start/stop/health-check)
- ❌ Extra port allocation
- ❌ `pip install 'supertonic[serve]'` requires extra deps (fastapi + uvicorn)
- ❌ Need to handle server crash recovery

**Effort:** ~3–4 hours (server lifecycle manager + HTTP client)

---

### Approach C: Native C# via ONNX Runtime (BEST QUALITY 🏆)

**Pattern:** Use `Microsoft.ML.OnnxRuntime` NuGet package to load the ONNX model directly in-process, no Python or HTTP server needed.

**C# side:**
```xml
<PackageReference Include="Microsoft.ML.OnnxRuntime" Version="*" />
<PackageReference Include="Microsoft.ML.OnnxRuntime.Managed" Version="*" />
```

Then create a direct ONNX inference wrapper. This requires:
- Understanding the model's input/output tensors
- Preprocessing text (tokenization)
- Postprocessing waveforms

**Pros:**
- ✅ Zero external dependencies — fully self-contained
- ✅ Lowest latency (no IPC, no HTTP, no Python)
- ✅ No uv/Python runtime needed at all
- ✅ Best fit for the PTT app's "low latency" philosophy

**Cons:**
- ❌ **High effort** — need to understand model architecture
- ❌ Supertonic doesn't publish a ready-made C# inference example (only Python/Node/Go)
- ❌ Tokenizer is custom (not a standard like SentencePiece)
- ❌ ONNX Runtime native binary adds ~50MB to deployment
- ❌ Might need the entire model forward pass re-implemented for C#

**Effort:** Weeks (research + implementation) unless Supertone publishes C# bindings

---

### Approach D: Hybrid — uv + supertonic serve (RECOMMENDED VARIANT)

**Pattern:** Use a managed uv-run HTTP server (like Approach B but with explicit process lifecycle management similar to CoquiUvTtsProcessor).

On startup, launch:
```bash
uv run --with 'supertonic[serve]' supertonic serve --host 127.0.0.1 --port 7788
```

Keep the process alive. On shutdown, kill it. On crash, restart it.

**Why this is the best compromise:**
- ✅ Long-running process = no per-request startup cost
- ✅ OpenAI-compatible endpoint = minimal C# code (copy OpenAiTtsProvider)
- ✅ uv handles Python/dependency management (same as Coqui)
- ✅ `/v1/health` = clean readiness check
- ✅ Can import custom Voice Builder voices dynamically
- ✅ Async fully supported

---

## 4. Recommended Provider (Short-Term)

**Approach A (uv subprocess)** — because:

1. **Blueprint exists**: `CoquiUvTtsProvider` + `CoquiUvProcessRunner` can be adapted in ~50 lines
2. **Easiest to implement and test**: No HTTP server, no port conflicts, no new patterns
3. **Same uv dependency**: If the user already has Coqui working, uv is already there
4. **Voice cloning**: Just pass the JSON file path as a voice parameter
5. **Future**: Can be upgraded to Approach B/D later without changing the ITextToSpeech contract

**Implementation sketch:**

```
Services/AgentOutput/TextToSpeach/Providers/Supertonic/
├── SupertonicTtsProvider.cs      # ITextToSpeech implementation
├── SupertonicService.py          # Python service script (embedded resource)
├── SupertonicProcessRunner.cs    # (Optional) if more complex than Coqui's pattern
```

**Code diff summary (TtsService.cs):**
```csharp
TtsProviderType.Supertonic => new Providers.SupertonicTtsProvider(
    console,
    cfg.CustomDataDir ?? cfg.DataDir,
    voice: cfg.TtsVoice ?? "M1",
    debugLog: true),
```

---

## 5. Recommended Provider (Medium-Term)

**Approach B/D (local HTTP server)** — switch to this once basic integration works.

Reasons:
- Performance (hot server, no per-request overhead)
- Minimal C# code (OpenAI-compatible endpoint)
- Rich API (health check, style import, batch)
- Can fall back to Approach A if server fails to start

---

## 6. Files to Create/Modify

### New files:
1. `src/OpenClawPTT/code/Services/AgentOutput/TextToSpeach/Providers/Supertonic/SupertonicTtsProvider.cs`
2. `src/OpenClawPTT/code/Services/AgentOutput/TextToSpeach/Providers/Supertonic/SupertonicService.py`
3. `src/OpenClawPTT/code/Services/Config/Wizard/SupertonicTtsConfigFlow.cs`

### Modified files:
1. `src/OpenClawPTT/code/Services/AgentOutput/TextToSpeach/TtsService.cs`
   - Add `Supertonic` to `TtsProviderType` enum
   - Add switch case in `CreateProvider()`

2. `src/OpenClawPTT/code/Services/Config/AppConfig.cs`
   - Add `TtsSupertonicVoice` (default: "M1")
   - Add `TtsSupertonicLang` (default: "en")
   - Add `TtsSupertonicQuality` (default: 8, range 5–12)
   - Add `TtsSupertonicSpeed` (default: 1.05, range 0.7–2.0)
   - Add description entries

3. `src/OpenClawPTT/code/Services/Config/Wizard/TtsConfigSection.cs`
   - Add `("Supertonic 3", "Supertonic")` to `TtsProviderOptions`
   - Add `AddSupertonicItems()` method

---

## 7. Python Script Design

Following the Coqui pattern but simpler (Supertonic's Python SDK is cleaner):

```python
"""supertonic_service.py — Long-running Supertonic TTS worker.
Communicates via JSON lines on stdin/stdout.

Protocol:
  {"id": "<guid>", "text": "...", "voice": "M1", "lang": "en",
   "quality": 8, "speed": 1.05}
→ {"id": "<guid>", "path": "/tmp/supertonic_<guid>.wav", "time": 0.15}
→ {"id": "<guid>", "error": "message"}
"""

import sys, json, os, tempfile, time, traceback
import soundfile as sf
from supertonic import TTS

tts = TTS(auto_download=True)
VOICE_CACHE = {}

def get_voice(name):
    if name not in VOICE_CACHE:
        VOICE_CACHE[name] = tts.get_voice_style(voice_name=name)
    return VOICE_CACHE[name]

for line in sys.stdin:
    line = line.strip()
    if not line:
        continue
    try:
        req = json.loads(line)
        text = req["text"]
        voice = get_voice(req.get("voice", "M1"))
        lang = req.get("lang", "en")
        quality = req.get("quality", 8)
        speed = req.get("speed", 1.05)
        req_id = req.get("id", "?")

        t0 = time.time()
        wav, duration = tts.synthesize(text, voice_style=voice, lang=lang,
                                        total_steps=quality, speed=speed)
        elapsed = time.time() - t0

        out = tempfile.mktemp(suffix=".wav", prefix=f"supertonic_{req_id}_")
        sf.write(out, wav.squeeze(), 44100)
        result = json.dumps({"id": req_id, "path": out,
                             "time": round(elapsed, 3),
                             "bytes": os.path.getsize(out)})
        sys.stdout.write(result + "\n")
        sys.stdout.flush()
    except Exception as e:
        err = json.dumps({"id": req.get("id", "?"),
                          "error": str(e),
                          "trace": traceback.format_exc()})
        sys.stdout.write(err + "\n")
        sys.stdout.flush()
```

---

## 8. Provider Class Design (C#)

Heavily based on `CoquiUvTtsProvider` — same `SemaphoreSlim`, same `_pending` dictionary, same `EnsureRunningAsync()`:

```csharp
namespace OpenClawPTT.TTS.Providers;

public sealed class SupertonicTtsProvider : ITextToSpeech, IAsyncDisposable
{
    private readonly string _dataDir;
    private readonly string _defaultVoice;
    private readonly string _defaultLang;
    private readonly int _quality;
    private readonly double _speed;
    private readonly IColorConsole _console;
    private readonly bool _debugLog;

    // Process management (adapt from CoquiUvProcessRunner)
    private readonly SupertonicProcessRunner _processRunner;
    private readonly SemaphoreSlim _sem = new(1, 1);
    private ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new();

    public string ProviderName => "Supertonic 3 TTS";

    public IReadOnlyList<string> AvailableVoices { get; } =
        ["M1", "M2", "M3", "M4", "M5", "F1", "F2", "F3", "F4", "F5"];

    public IReadOnlyList<string> AvailableModels { get; } = ["supertonic-3"];

    // Same SynthesizeAsync pattern as CoquiUvTtsProvider
    // ...
}
```

---

## 9. Summary

| Approach | Effort | Quality | Dependencies | Latency |
|----------|--------|---------|--------------|---------|
| A: uv subprocess | **Low** (~2-3h) | Good | Python + uv (existing) | Medium |
| B: HTTP server | Medium (~3-4h) | Great | Python + uv + fastapi | Low |
| C: Native ONNX | **High** (~weeks) | Best | ONNX Runtime NuGet | Lowest |
| D: Managed server | Medium (~3-4h) | Great | Python + uv + fastapi | Low |

**Recommendation for first pass:** **Approach A** (uv subprocess) — blueprint from CoquiUvTtsProvider, minimal risk, can be done in an afternoon.

**Upgrade path:** Approach A → Approach D (switch from direct subprocess to managed HTTP server) → Approach C (eventually, if ONNX C# bindings mature).

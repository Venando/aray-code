# Direct LLM Pipeline Analysis — Resilience & Status Updates

## Overview

The Direct LLM pipeline allows the PTT client to bypass the OpenClaw agent and send messages directly to an LLM (OpenAI-compatible or Anthropic). Used for three features:
1. `/llm` command (user-triggered direct messaging)
2. TTS summarization (automatic, agent reply → summary → TTS)
3. Conversation naming (automatic, message-driven title generation)

## Full Pipeline Flow

```
Configuration
 │
 ├─ AppConfig.DirectLlmUrl / DirectLlmModelName / DirectLlmToken / DirectLlmApiType
 ├─ DirectLlmConfigSection (wizard: URL → Token → Model → API type)
 ├─ AppConfigCommand (/appconfig DirectLlmUrl ...)
 │
 ▼
ServiceFactory.CreateDirectLlmService(AppConfig)
 │
 ├─ Normal mode: new DirectLlmService(cfg) — real HttpClient
 └─ Test mode: new MockDirectLlmService(scenario, console) — canned responses
 │
 ▼
AppRunner.RunAppLoopAsync
 │
 ├─ Creates DirectLlmService (line 86)
 ├─ Passes to TtsSummarizer (line 88, null if not configured)
 ├─ Passes to DirectLlmProbeService for startup probe (line 94)
 ├─ Passes to CreateNamingPipeline → ConversationNamingService (line 190)
 └─ Passes to CreateShellAndHotkeyServicesAsync → StreamShellInputHandler (null if not configured)
 │
 ▼
DirectLlmProbeService (Startup + Config-Change Re-probe)
 │
 ├─ ProbeOnStartupAsync → ProbeAndUpdateAsync → service.ProbeAsync()
 └─ OnConfigSaved → fresh service → ProbeAndUpdateAsync
 │
 ▼
Consumers
 │
 ├─ LlmCommand (via /llm message|summary-test|title-test)
 │   └─ guarded: null || !IsConfigured → yell at user
 │
 ├─ TtsSummarizer.SummarizeForTtsAsync (agent reply → summary)
 │   └─ guarded: null || !IsConfigured → throw InvalidOperationException
 │
 └─ ConversationNamingService.GenerateNameAsync (adaptive titles)
     └─ guarded: null || !IsConfigured → skip silently
 │
 ▼
DirectLlmService.SendAsync(string, CancellationToken)
 │
 ├─ apiType switch:
 │   ├─ "anthropic-messages" → SendAnthropicAsync
 │   └─ default → SendOpenAiAsync (also handles "openai-chat" → "openai-completions")
 │
 ▼
HTTP Request
 │
 ├─ OpenAI: POST <url>/v1/chat/completions, Bearer token, deserialize OpenAiResponse
 └─ Anthropic: POST <url>/v1/messages, x-api-key header, filter "text" blocks
 │
 ▼
Response extracted → returned to consumer
```

## Resilience Analysis

### ✅ What's Good

| Aspect | Detail |
|--------|--------|
| **HttpClient timeout** | 5 min — good for slow local LLMs (Ollama cold start) |
| **Nullable service pattern** | `IDirectLlmService?` passed as null when unconfigured; all consumers check before use |
| **Cancellation propagation** | `CancellationToken` flows through all paths (except naming — see below) |
| **Dispose pattern** | Correctly implemented with `_disposed` guard + ObjectDisposedException |
| **Error handling in consumers** | All three consumers wrap calls in try/catch with user-facing messages |
| **Dynamic command registration** | `/llm` shown/hidden based on config state, not just startup value |
| **URL builder** | Handles bare host, `/v1`, and full path variants |
| **Probe isolation** | Uses `ResponseHeadersRead` for quick header-only check; `max_tokens=1` |

### 🔴 Issues Found

#### 1. No HTTP Resilience — Single Shot, No Retry

`DirectLlmService` creates one `HttpClient`, no retry policy, no circuit breaker, no fallback. Every failure is final.

**Impact:** Transient network blips → user sees "LLM request failed". No retry for TTS summarization (could drop audio output). No retry for naming (uses CancellationToken.None, hangs indefinitely).

```csharp
// DirectLlmService.cs — single unconditional send
using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
response.EnsureSuccessStatusCode();  // throws on any non-2xx
```

**Recommendation:** Add retry with exponential backoff (e.g., Polly), at least for TTS summarization which impacts user-facing audio.

---

#### 2. URL Builder Trailing-Slash Bug

```
"http://localhost:11434/v1/" → "http://localhost:11434/v1//chat/completions"
```

`BuildOpenAiUrl` uses `TrimEnd('/')` before appending `/chat/completions`. But `url.EndsWith("/v1/")` matches first, which also calls `TrimEnd('/')`, producing the same result for that path. 

Actually re-reading: all three branches use `TrimEnd('/')` before appending, so no actual double-slash. But let me verify:

```csharp
// Branch 1: full path → return as-is (no double slash possible)
if (url.EndsWith("/v1/chat/completions")) return url;

// Branch 2: /v1 or /v1/ → TrimEnd('/') + "/chat/completions"  
//   /v1/ → /v1 + /chat/completions = /v1/chat/completions ✓
//   /v1 → /v1 + /chat/completions = /v1/chat/completions ✓

// Branch 3: host only → TrimEnd('/') + "/v1/chat/completions"
//   http://host → http://host/v1/chat/completions ✓
//   http://host/ → http://host + /v1/chat/completions = http://host/v1/chat/completions ✓
```

Actually this is fine. No double-slash. Good.

---

#### 3. No Per-Request Timeout — Single HttpClient Timeout

All requests share the 5-minute `HttpClient.Timeout`. No way to set a shorter timeout for probe (8s is hardcoded in probe method, not on the client). Long-running or stuck requests block the HttpClient for 5 minutes.

**Recommendation:** Use per-request `CancellationTokenSource.CreateLinkedTokenSource(ct)` with individual timeout, like the probe already does.

---

#### 4. Anthropic MaxTokens Hardcoded to 4096

```csharp
private async Task<string> SendAnthropicAsync(string message, CancellationToken ct)
{
    var requestBody = new AnthropicRequest
    {
        ...
        MaxTokens = 4096,  // hardcoded!
    };
```

OpenAI uses default (no MaxTokens), which means it uses the model's default. Anthropic is capped at 4096 regardless of model capability. If the user's Anthropic model supports 8K or 16K output, the Direct LLM won't use it.

---

#### 5. No Streaming Support

All responses are fully buffered. For long responses (TTS summarization, conversation naming), the user sees no intermediate feedback. The `/llm message` command shows "Sending..." then waits potentially minutes.

---

#### 6. ConversationNamingService Uses CancellationToken.None

```csharp
var name = await _directLlm.SendAsync(prompt, CancellationToken.None);
```

While TTS summarizer properly passes `ct` through, conversation naming ignores it entirely. On shutdown during a naming request, the task runs to completion (or 5-min timeout) despite cancellation.

---

#### 7. Re-probe Creates New Service — Original Remains Stale

When DirectLlmUrl changes via `/appconfig`:

1. `DirectLlmProbeService.OnConfigSaved` creates `freshService = _factory.CreateDirectLlmService(e.NewConfig)`
2. Probes with the new service ✓
3. But the **original** `directLlmService` captured in `AppRunner.RunAppLoopAsync` still points to the old config
4. `/llm command` still uses the stale service reference

So: status updates correctly on config change, but actual `/llm message` still uses old URL/model until restart. The code comment in `StreamShellInputHandler` acknowledges this:

> "A future improvement could re-create IDirectLlmService from IServiceFactory here."

---

#### 8. 8-Second Probe Timeout — Hardcoded

```csharp
timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));
```

Local LLMs (especially on first load or model swap) can take much longer than 8 seconds. A cold-start DeepSeek or Mistral via Ollama can take 15-30 seconds.

---

#### 9. No Periodic Health Check

Probe runs once at startup and once on config change. If the LLM dies after startup (OOM, crash, network blip), the status stays Green and all `/llm` calls silently fail. The status reflects stale information.

---

#### 10. ServiceFactory.CreateDirectLlmService Creates New HttpClient Each Time

```csharp
public virtual IDirectLlmService CreateDirectLlmService(AppConfig cfg)
    => new DirectLlmService(cfg);  // new HttpClient inside constructor
```

Each re-probe from config change → new HttpClient with new TCP connection. No connection pooling. Over many config changes, sockets accumulate.

---

## Status Update Analysis

### Status Lifecycle

```
Startup:
  CreateDirectLlmService → instantiated (no status change yet)
  ProbeOnStartupAsync called (Task.Run, parallel)
    → Yellow (probing)
    → Green (ok) or Red (fail)
  AppRunner uses IsConfigured → null or configured stream

Runtime (/appconfig change):
  DirectLlmProbeService.OnConfigSaved
    → creates fresh service
    → ProbeAndUpdateAsync
      → Yellow → Green/Red

Consumers (LlmCommand, TtsSummarizer, Naming):
  → No status updates on success or failure
```

### ✅ What's Good

| Aspect | Detail |
|--------|--------|
| **Startup probe** | Runs in background, doesn't block app start |
| **Correct transitions** | Yellow→Green/Red, covers not-configured case |
| **Cancel/exception handled** | Cancel → Yellow, Exception → Red with logging |
| **Config-change re-probe** | Detects DirectLlmUrl/DirectLlmModelName changes and re-probes |
| **Standard infrastructure** | Uses `StatusService.SetServiceStatus(ServiceKind.DirectLlm, StatusColor)` |

### 🔴 Issues Found

#### 1. Status Not Updated on Actual Send Failures

The probe tests liveness (just `max_tokens=1` "hi"). It doesn't test the actual message pipeline. If the LLM accepts probes but fails on real requests (e.g., context length exceeded, model unloaded after probe, auth token expired), the status stays Green while all `/llm message` calls fail.

```
Healthy flow:
  Probe OK → Green ✓
  /llm message fails → stays Green ✗
  TTS summarization fails → stays Green ✗
```

**Recommendation:** Track runtime failures and degrade status. E.g., after N consecutive `/llm` send failures → set Red (or set Yellow after first failure, Red after 3).

---

#### 2. No Runtime-Success Status for TTS Summarization or Naming

These are significant features that depend on Direct LLM, but their success/failure is invisible in the status bar. A broken summarization pipeline (e.g., TTS code block mode parsing issue) silently fails with an exception caught by LlmCommand but no status update.

---

#### 3. Mock ErrorRecovery Scenario Doesn't Update Status

`MockDirectLlmService` throws `InvalidOperationException` every 3rd message in ErrorRecovery scenario. The status stays Green (from mock probe always returning true) even as sends fail.

---

#### 4. Race Condition: Startup Status Set Twice

In `AppRunner.RunAppLoopAsync`:
```csharp
// Line 93-94: probe task starts (background)
var llmProbeTask = Task.Run(() => llmProbeService.ProbeOnStartupAsync(...), ct);
```
The ProbeAsync inside sets status. But before the task runs, the DirectLlmService is created. If the probe hasn't started yet and a user immediately types `/llm message`, the status is at its default (initialized in `ServiceStatusPart` constructor to Yellow).

This is fine actually — Yellow is the correct default (not-yet-probed). No issue here.

---

#### 5. Status Bar Shows Yellow When Not Configured

When `DirectLlmUrl` and `DirectLlmModelName` are empty, `ProbeAndUpdateAsync` sets Yellow. This is the correct state (available but idle/unconfigured), matching the Gateway/STT pattern. Not a bug, just worth noting.

---

## All Usage Cases (Complete Map)

| # | Case | Trigger | Consumer | Status Impact |
|---|------|---------|----------|---------------|
| 1 | Startup probe | `AppRunner.RunAppLoopAsync` (Task.Run) | `DirectLlmProbeService.ProbeOnStartupAsync` | 🟡→🟢/🔴 |
| 2 | Config-change re-probe | `/appconfig DirectLlmUrl ...` → `ConfigSaved` event | `DirectLlmProbeService.OnConfigSaved` | 🟡→🟢/🔴 |
| 3 | `/llm message <text>` | User types `/llm message hello` | `LlmCommand.ExecuteMessageAsync` | ❌ No status change |
| 4 | `/llm summary-test` | User types `/llm summary-test` | `LlmCommand.ExecuteSummaryTestAsync` | ❌ No status change |
| 5 | `/llm title-test` | User types `/llm title-test` | `LlmCommand.ExecuteTitleTestAsync` | ❌ No status change |
| 6 | TTS summarization | Agent reply received, TTS enabled | `TtsSummarizer.SummarizeForTtsAsync` | ❌ No status change |
| 7 | Conversation naming (initial) | After 1st user message | `ConversationNamingService.GenerateNameAsync` | ❌ No status change |
| 8 | Conversation naming (adaptive) | After 6 more messages | `ConversationNamingService.GenerateNameAsync` | ❌ No status change |
| 9 | Dynamic `/llm` show/hide | Config saved with new DirectLlmUrl/Model | `StreamShellInputHandler.OnConfigSaved` | ❌ No status change |
| 10 | `/appconfig DirectLlmApiType` | API type switched | `DirectLlmService.SendAsync` (apiType switch) | ❌ No status change |

---

## Risk Summary

| Risk | Severity | Category |
|------|----------|----------|
| Send failures invisible in status | **High** | Status staleness |
| No retry on transient failures | **Medium** | Resilience |
| Conversation naming non-cancellable | **Medium** | Shutdown safety |
| Probe timeout too short for cold LLMs | **Medium** | False negatives |
| Stale service after config change | **Medium** | Inconsistency |
| No periodic health check | **Low** | Status staleness |
| Socket accumulation on repeated re-probes | **Low** | Resource leak |
| Hardcoded Anthropic MaxTokens | **Low** | Feature limit |
| No streaming for long responses | **Low** | UX |

## Recommendations (Priority Order)

1. **Track send failures in status** — After N consecutive send failures (from any consumer), set DirectLlm status to Red. On next successful send, revert to Green.
2. **Add retry** — At least 1-2 retries with backoff for TTS summarization (user-facing audio).
3. **Fix naming cancellation** — Pass `ct` instead of `CancellationToken.None` in `ConversationNamingService`.
4. **Increase probe timeout to 30s** — Or make it configurable via `ProbeTimeout` setting.
5. **Add periodic re-probe** — E.g., every 60 seconds while idle, to detect dead LLMs.
6. **Re-create DirectLlmService on config change** — Replace the old service in AppRunner so `/llm` uses new settings without restart.
7. **Reuse HttpClient** — Either make it static/shared, or use `IHttpClientFactory` to avoid socket accumulation.
8. **Add per-request cancellation** — Use linked CTS with individual timeouts (like probe already does) instead of relying on the single 5-min HttpClient timeout.
9. **Make Anthropic MaxTokens configurable** — Add `DirectLlmMaxTokens` setting or use a sensible default relative to model capability.

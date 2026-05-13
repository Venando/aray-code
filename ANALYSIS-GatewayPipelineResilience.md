# Gateway Pipeline Resilience & Status Analysis

**Date**: 2026-05-13
**Branch**: `analysis/gateway-pipeline-resilience`
**Base**: `origin/main` @ `9df155c`

## 1. Pipeline Overview

```
┌──────────┐     ┌────────────────┐     ┌─────────────────────┐
│ AppConfig│────▶│ ServiceFactory  │────▶│ GatewayClient       │
│ (JSON)   │     │ CreateGateway   │     │ (thin coordinator)  │
└──────────┘     │ Service()       │     └──────────┬──────────┘
                 └────────────────┘                │
                                                   ▼
                 ┌────────────────┐     ┌─────────────────────┐
                 │ AppRunner      │     │ GatewayConnection   │
                 │ wires status   │     │ Lifecycle           │
                 │ events ────────┼────▶│ (WS handshake,      │
                 │ Connected→Green│     │  V3 auth, snapshot) │
                 │ Disconn→Red    │     └──────────┬──────────┘
                 │ Recon→Yellow   │                │
                 └────────────────┘                ▼
                                        ┌─────────────────────┐
                                        │ GatewayMessager     │
                                        │ (receive pump,      │
                                        │  frame dispatch,    │
                                        │  disconnect detect) │
                                        └──────────┬──────────┘
                                                   │
                                        ┌──────────▼──────────┐
                                        │ GatewayReconnector  │
                                        │ (backoff retry loop)│
                                        └─────────────────────┘
```

### 1.1 Configuration → Connection

1. `AppConfig.GatewayUrl` (and `AuthToken`, `DeviceToken`, `TlsFingerprint`) is loaded from JSON
2. `ServiceFactory.CreateGatewayService(cfg)` creates:
   - `DeviceIdentity` — generates/loads Ed25519 keypair for V3 protocol auth
   - `GatewayEventSource` — simple event bus (no state, just multicast delegates)
   - `GatewayClient` — owns lifecycle, delegates connect/send/disconnect
   - `GatewayService` — wraps client, wires event handlers to output coordinator
3. `AppRunner.RunAppLoopAsync()` subscribes to gateway lifecycle events for status bar
4. `TryConnectWithGuidanceAsync()` sets Yellow (connecting), calls `gateway.ConnectAsync()`
5. On failure: classifies error via `GatewayErrorClassifier`, logs to `ErrorLogStore`, sets Red

### 1.2 Connection Lifecycle (detailed)

`GatewayConnectionLifecycle.ConnectAsync()`:
1. Acquires `ReconnectLock` semaphore
2. `DisposeConnection(ct)` — cleanup any prior connection
3. `ConnectWebSocketAndHandshakeAsync(ct)`:
   - `ClientWebSocket.ConnectAsync(uri)` — TCP + TLS + WS upgrade
   - Creates `GatewayMessager` (owns `MessageFraming` for request/response correlation)
   - Starts `ReceiveLoop` background task
   - Waits for `connect.challenge` event (10s timeout)
   - Builds V3 signature payload (device public key, nonce, scopes, signedAt)
   - Sends `connect` request (30s timeout)
   - Validates `hello-ok` response
   - Processes hello snapshot → `SnapshotProcessor` → populates `AgentRegistry`
   - Persists device token if issued
4. `CompleteAuthenticationAsync(linkedCt)`:
   - Subscribes to session via `sessions.subscribe`
   - Wires `AgentRegistry.ActiveSessionChanged` for agent-switch resubscription
5. Fires `ConnectionSucceeded` event

### 1.3 Disconnect Detection & Reconnection

`GatewayMessager.ReceiveLoop()` is the single receive pump running on a background thread:
- **Normal close**: detects `WebSocketMessageType.Close` → fires `Disconnected` → triggers reconnection
- **WebSocketException**: fires `Disconnected` → triggers reconnection
- **Other exceptions**: fires `Disconnected` → triggers reconnection
- **Cancellation**: `OperationCanceledException` is expected on dispose — no reconnection

`GatewayReconnector.ReconnectLoopAsync()`:
- Exponential backoff: `baseDelay * 2^(attempt-1)`, capped at 60s
- Max retry count: 5 (configurable via `MaxRetryCount`)
- Uses `GatewayErrorClassifier.Classify()` on each attempt failure to check if retryable
- Non-retryable errors (auth, pairing) break the loop early with guidance
- Fatal errors suggest app restart
- On all-attempts-exhausted: fires `ReconnectFailed` → relays to `_events.RaiseDisconnected()` → Red status

### 1.4 Event Dispatch Pipeline

`GatewayMessager.HandleEvent()` processes inbound frames:
1. **Agent status extraction**: `AgentStatusExtractor.Extract()` runs on ALL payloads BEFORE filtering
2. **One-shot event waiters**: resolved via `MessageFraming.ResolveEventWaiter()`
3. **chat.side_result**: bypasses session key filter (side results target the query, not active session)
4. **Session key filter**: drops events not matching `AgentRegistry.ActiveSessionKey`
5. **Typed dispatch**:
   - `session.message` / `agent` / `chat` → `SessionMessageHandler` → extracts content, fires `AgentReplyFull`/`AgentReplyDelta`/`AgentThinking`/`AgentToolCall` on `GatewayEventSource`
   - `model.failover` → `ModelFallbackHandler`
   - `exec.approval.requested` → auto-approval via `HandleApprovalRequest()`
   - Everything else → `GatewayEventHandler` (logs)

### 1.5 Status Update Chain

```
GatewayConnectionLifecycle        GatewayClient        GatewayService        AppRunner
──────────────────────────        ─────────────        ──────────────        ─────────
ConnectionSucceeded ──────────▶   ConnectionSucceeded  ▶ Connected ──────▶   SetStatus(Green)
                                                                            
Reconnecting ─────────────────▶   Reconnecting ──────▶ Reconnecting ────▶   SetStatus(Yellow)
                                                                            
[ReconnectFailed]                                                             
  → RaiseDisconnected() ──────▶   (via EventSource) ─▶ Disconnected ────▶   SetStatus(Red)
                                                                            
ReceiveLoop exception:                                                        
  RaiseDisconnected() ────────▶   (via EventSource) ─▶ Disconnected ────▶   SetStatus(Red)
```

Status colors:
- **Green** (●): Connected / operational
- **Yellow** (animated •●•●): Connecting / reconnecting / in transition
- **Red** (●): Disconnected / error

`ServiceStatusPart` renders as e.g. ` GW:●` with Spectre markup color tags. Yellow state uses `AlwaysRebuild = true` and cycles through 4 animation frames at 600ms intervals via `StatusAnimationManager`.

---

## 2. Resilience Analysis

### 2.1 What Works Well

| Area | Assessment |
|------|-----------|
| **Error classification** | `GatewayErrorClassifier` distinguishes Transient/Actionable/Fatal with rich detail extraction from JSON payloads. Non-retryable errors (PAIRING_REQUIRED, AUTH_DEVICE_TOKEN_MISMATCH) correctly break the retry loop early. |
| **Reconnection backoff** | Exponential backoff with configurable base delay, capped at 60s. Prevents hammering the gateway. |
| **Reconnect lock** | `SemaphoreSlim(1,1)` prevents concurrent connect attempts. `_isReconnecting` guard prevents double-scheduling during an active reconnect loop. |
| **Cancellation propagation** | `_disposeCts` is linked into all async operations. `OperationCanceledException` during reconnect correctly stops the loop. |
| **Error logging** | `ErrorLogStore` persists classified errors to `diagnostics/errors.json` with bounded capacity (1000 entries). Thread-safe via `ReaderWriterLockSlim`. |
| **Auto-approval** | `exec.approval.requested` events are auto-handled with a background job — prevents approval deadlocks. |
| **Config-change reconnect** | Gateway config changes (`/reconfigure`) recreate the client and reconnect on a background thread with status transitions. |
| **Session-key filtering** | Events not matching the active agent session are dropped, preventing cross-session noise. |
| **Side-result bypass** | `chat.side_result` events correctly bypass the session key filter since they target the query session, not the active one. |

### 2.2 Weak Spots

| Area | Risk | Severity |
|------|------|----------|
| **No overall connect timeout** | `ClientWebSocket.ConnectAsync` may not respect CancellationToken for the TCP/TLS phase. If the gateway is unreachable but the TCP SYN doesn't fail (e.g., firewall dropping packets silently), the connection could hang indefinitely. Individual steps have timeouts (challenge wait: 10s, request: 30s) but the WS upgrade itself has no timeout wrapper. | **Medium** |
| **ReceiveLoop buffer saturation warning** | When a fragment fills the 512KB buffer, a warning is logged but no mitigation happens. Large frames (e.g., huge snapshots) could cause data loss or frame corruption. | **Low** |
| **Fire-and-forget in disconnect path** | `_onDisconnection?.Invoke(ct)` delegates to `HandleDisconnectionAsync` which uses `_ = ScheduleReconnectAsync(ct)`. If the disconnect handling throws, the exception may go unobserved. Currently mitigated by try-catch inside HandleDisconnectionAsync. | **Low** |
| **Single receive pump** | `ReceiveLoop` is a single loop — if it blocks on processing a large frame, no other frames are received. The `ProcessFrame` call is synchronous within the loop. | **Low** |
| **ReconnectLock held for entire ConnectAsync** | During reconnect, the `ReconnectLock` is held for the entire connection handshake (seconds). New disconnect events during this time are blocked at `ScheduleReconnectAsync` but won't schedule a new reconnect. However, `_isReconnecting` flag prevents double-scheduling, and the active reconnect loop handles its own retries. | **Info** |

---

## 3. Status Update Correctness — Issues Found

### 🔴 Issue 1: `GatewayService.ConnectAsync` fires `Connected` event TWICE (HIGH)

**File**: `GatewayService.cs` line ~90
**Root cause**: `Connected` fires from two paths on the same connection:
1. **During** `ConnectAsync`: `GatewayConnectionLifecycle.ConnectionSucceeded` → `GatewayClient.ConnectionSucceeded` → `GatewayService.Connected` (via `InitGatewayClient` relay)
2. **After** `ConnectAsync` returns: explicit `Connected?.Invoke()` call

```csharp
// GatewayService.cs
public async Task ConnectAsync(CancellationToken ct)
{
    await _gatewayClient.ConnectAsync(ct);  // ← fires Connected via event relay
    Connected?.Invoke();                     // ← fires Connected AGAIN
}
```

And in `InitGatewayClient`:
```csharp
gc.ConnectionSucceeded += () => Connected?.Invoke();  // ← first fire
```

**Impact**: On initial connection, `Connected` fires twice. On config-change reconnect, it fires from both the event chain AND the explicit `SetStatus(Green)` in the fire-and-forget task (three times). Currently harmless because `SetStatus(Green)` is idempotent, but any future non-idempotent subscriber would break.

**Fix**: Remove the explicit `Connected?.Invoke()` from `GatewayService.ConnectAsync`. The event relay from `ConnectionSucceeded` already covers both initial and reconnection paths.

---

### 🟡 Issue 2: `Disconnected` event fires twice on permanent reconnect failure (MEDIUM)

**Root cause**: Two paths fire `Disconnected`:
1. `ReceiveLoop` catch block: `_events.RaiseDisconnected()` — fires on initial disconnect
2. `ReconnectFailed` relay: `_gatewayReconnector.ReconnectFailed += () => _events.RaiseDisconnected()` — fires after all retries exhausted

**Sequence**: Green → Red (disconnect) → Yellow (reconnecting) → Red (reconnect failed)

The second Red is correct behavior (reconnect failed), but it goes through `Disconnected` event which semantically means "we just detected a disconnect", not "reconnection failed". The `RaiseDisconnected()` from `ReconnectFailed` conflates two distinct states:
- **Actually disconnected** (socket dropped)
- **Reconnection exhausted** (socket dropped, tried to recover, gave up)

**Impact**: Subscribers to `Disconnected` get it twice on permanent failure. Currently harmless (Red is idempotent), but semantically misleading.

**Fix**: Add a `ReconnectFailed` event to `IGatewayEventSource` and use it instead of `RaiseDisconnected()`, or add a guard in `GatewayConnectionLifecycle` to track whether a disconnect was already raised.

**Alternative**: Accept the double-fire since it's idempotent, but document the behavior.

---

### 🟡 Issue 3: `HandleGatewayConfigChanged` duplicates status setter path (LOW)

**File**: `AppRunner.ConfigSetup.cs`
**Root cause**: During config-change reconnect, status is set through TWO independent paths:
1. **Event chain**: `RecreateWithConfig` → new `GatewayClient` → `ConnectionSucceeded` → `GatewayService.Connected` → AppRunner handler → `SetStatus(Green)`
2. **Direct**: Fire-and-forget task calls `_statusService.SetServiceStatus(Gateway, Green)` directly

```csharp
_ = Task.Run(async () =>
{
    try
    {
        await gateway.ConnectAsync(CancellationToken.None);
        _statusService.SetServiceStatus(Gateway, Green);  // ← direct, redundant with event chain
    }
    catch
    {
        _statusService.SetServiceStatus(Gateway, Red);     // ← direct, redundant with event chain
    }
});
```

**Impact**: Status set redundantly via event + direct call. Idempotent but indicates a pattern where handlers don't fully trust the event system.

**Fix**: Remove the direct `SetServiceStatus` calls in the fire-and-forget task and rely on the event chain alone. The events are properly wired by `RecreateWithConfig` → `InitGatewayClient`.

---

### 🟢 Issue 4: `Reconnecting` event fires before the first delay (INFO)

**Root cause**: `ScheduleReconnectAsync` fires `ReconnectStarted` (→ `Reconnecting` → Yellow) BEFORE the backoff delay. The reconnect loop then waits `CalculateBackoffDelay(attempt)` before actually attempting.

```csharp
public async Task ScheduleReconnectAsync(CancellationToken ct)
{
    // ... takes lock, sets _isReconnecting, releases lock
    ReconnectStarted?.Invoke();  // ← Yellow set here
    _reconnectTask = ReconnectLoopAsync(ct); // ← waits baseDelay before first attempt
}
```

**Impact**: Yellow status shows immediately on disconnect, then the user waits `ReconnectDelaySeconds * 1` (default 1.5s) before the first reconnect attempt. This is arguably correct — "I'm reconnecting" starts when the decision to reconnect is made, not when the first attempt begins.

**Fix**: None needed. This is correct behavior.

---

### 🟢 Issue 5: TTS wire task observed in Dispose but not in error path (INFO)

**File**: `GatewayService.cs`
**Root cause**: `_ttsWireTask` is created in the constructor (`WireTtsOnProviderReadyAsync`) and observed in `Dispose()`. If the TTS provider task fails, the catch block in `WireTtsOnProviderReadyAsync` catches the exception. If the `Dispose` path observes it and it's already faulted (but caught internally), `GetAwaiter().GetResult()` is safe.

```csharp
public void Dispose()
{
    // ...
    try { _ttsWireTask?.GetAwaiter().GetResult(); }
    catch (OperationCanceledException) { /* expected */ }
    catch (Exception ex) { _console?.LogError("gateway", $"TTS wire task threw: {ex.Message}"); }
}
```

**Assessment**: Defensive and correct. The task catches its own exceptions, so `GetResult()` won't re-throw except for cancellation. Well-handled.

---

### 🟢 Issue 6: `GatewayMessager.ClearFraming()` called by DisposeConnection but not on all error paths (INFO)

**Root cause**: `GatewayConnectionLifecycle.DisposeConnection(ct)` calls `_gatewayMessager?.Dispose()` which calls `ClearFraming()` which calls `ClearPendingRequests()` and `ClearEventWaiters()`. This is the correct cleanup for in-flight request TCS's. However, if `DisposeConnection` throws before reaching the messager cleanup (unlikely since it catches WebSocket close exceptions), the framing could be left with unresolved TCSs.

**Impact**: Minimal — `DisposeConnection` is robust about catching exceptions during WebSocket close.

**Fix**: None needed.

---

## 4. Status State Machine

```
                    ┌──────────────┐
          ┌────────▶│   YELLOW     │◀──────────────┐
          │         │ (connecting) │                │
          │         └──────┬───────┘                │
          │                │                        │
          │         connect success                 │ reconnect
          │                │                        │ started
          │                ▼                        │
          │         ┌──────────────┐                │
          │         │    GREEN     │                │
          │         │ (connected)  │                │
          │         └──────┬───────┘                │
          │                │                        │
          │         disconnect                      │
          │         detected                        │
          │                │                        │
          │                ▼                        │
          │         ┌──────────────┐         ┌──────┴───────┐
          │         │     RED      │────────▶│   YELLOW     │
          │         │(disconnected)│ reconnect│ (reconnecting│
          │         └──────────────┘ started  └──────────────┘
          │                                            │
          │           permanent failure                │
          └────────────────────────────────────────────┘
                    (ReconnectFailed → Red)
```

### Transitions that are verified correct:

| Transition | Trigger | Handler |
|-----------|---------|---------|
| Yellow → Green | Connection succeeds | `Connected` event → `SetStatus(Green)` |
| Green → Red | Disconnect detected | `Disconnected` event → `SetStatus(Red)` |
| Red → Yellow | Reconnect started | `Reconnecting` event → `SetStatus(Yellow)` |
| Yellow → Red | Reconnect failed | `ReconnectFailed` → `Disconnected` → `SetStatus(Red)` |
| (initial) → Yellow | App starts connecting | `TryConnectWithGuidanceAsync` → `SetStatus(Yellow)` |
| Yellow → Red | Initial connect fails | `TryConnectWithGuidanceAsync` catch → `SetStatus(Red)` |
| Any → Yellow | Gateway config changed | `HandleGatewayConfigChanged` → `SetStatus(Yellow)` |

### Transitions with issues:

| Transition | Issue |
|-----------|-------|
| Yellow → Green | Fires via `Connected` event **and** explicit `Connected?.Invoke()` in `ConnectAsync` (Issue #1) |
| Yellow → Red (permanent fail) | Fires via `Disconnected` event twice — from `ReceiveLoop` and from `ReconnectFailed` relay (Issue #2) |

---

## 5. Recommendations

### Immediate (fixes for Issues #1–#2)

1. **Remove duplicate `Connected?.Invoke()`** from `GatewayService.ConnectAsync`. The `ConnectionSucceeded` event relay in `InitGatewayClient` already covers this.

2. **Separate `ReconnectFailed` from `Disconnected`**: Either:
   - Add a `ReconnectFailed` event to `IGatewayEventSource` and wire it to a new status handler, OR
   - Add a `_disconnectedRaised` guard in `GatewayConnectionLifecycle` that prevents `ReconnectFailed` from re-raising `Disconnected` if it was already raised, and instead have `ReconnectFailed` set Red directly through a new path

### Medium-term

3. **Add overall connect timeout**: Wrap `ClientWebSocket.ConnectAsync` with a `Task.WhenAny` + `Task.Delay(timeout)` to prevent indefinite hangs during TCP/TLS phase. Configurable via `AppConfig` (e.g., `ConnectTimeoutSeconds`, default 15s).

4. **Consider adding a status for "no gateway needed"**: Currently, when the initial connection fails with a non-fatal error and the app continues, status shows Red. A separate status like `Gray` (explicitly disconnected / not attempting) could distinguish "I tried and failed" from "I'm not trying."

### Low-priority

5. **Remove direct status setters from `HandleGatewayConfigChanged`**: Trust the event chain. If the event chain is broken during `RecreateWithConfig`, fix the event chain, don't work around it.

6. **Consider async processing for large frames**: If `ProcessFrame` takes significant time, consider dispatching to a channel or `Task.Run` to keep the receive pump responsive.

---

## 6. Files Examined

| File | Role |
|------|------|
| `Services/Config/AppConfig.cs` | Configuration model (GatewayUrl, AuthToken, etc.) |
| `Services/ServiceFactory.cs` | Dependency wiring |
| `Connection/GatewayClient.cs` | Thin coordinator over lifecycle |
| `Connection/GatewayService.cs` | Service layer, event wiring, TTS integration |
| `Connection/GatewayConnectionLifecycle.cs` | WS handshake, V3 auth, snapshot processing |
| `Connection/GatewayMessager.cs` | Receive pump, frame dispatch, disconnect detection |
| `Connection/GatewayReconnector.cs` | Exponential backoff retry loop |
| `Connection/GatewayEventSource.cs` | Event bus (multicast delegates) |
| `Connection/MessageFraming.cs` | Request/response correlation, event waiters |
| `Connection/SnapshotProcessor.cs` | Hello snapshot → AgentRegistry seeding |
| `Connection/Events/GatewayConnectionHandler.cs` | Logging for connect/disconnect events |
| `Services/Diagnostics/GatewayErrorClassifier.cs` | Error classification (Transient/Actionable/Fatal) |
| `Services/Diagnostics/ErrorLogStore.cs` | Persistent error log with bounded capacity |
| `Services/Status/StatusService.cs` | Status bar rendering coordination |
| `Services/Status/StatusParts/ServiceStatusPart.cs` | Individual status dot with animation |
| `Services/Status/StatusAnimationManager.cs` | 600ms animation timer for Yellow dots |
| `AppRunner.cs` | Top-level orchestration, status event subscriptions |
| `AppRunner.ConfigSetup.cs` | Config-change handlers for gateway/STT/TTS |

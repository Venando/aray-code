# Protocol v3 → v4 Research: What Changed & What ArayCode Should Update

**Date:** 2026-05-25
**Scope:** OpenClaw Gateway WebSocket Protocol Version 3 → 4
**Source:** OpenClaw source (`src/gateway/protocol/schema/frames.ts`, `client-info.ts`, `connect-error-details.test.ts`), docs (`docs/openclaw.ai/gateway/protocol`)

---

## Executive Summary

ArayCode **already sends** `minProtocol: 3, maxProtocol: 4` in its connect request, so it claims to support v4. However, the app **does not fully utilize** several v4-specific fields in the `hello-ok` response, and it **does not send** some optional v4 connect params that improve client identification. Additionally, new capability claims and error-handling paths introduced in v4 are not handled.

This document breaks down:
1. What protocol v4 added (connect params + hello-ok response)
2. What ArayCode currently does
3. What should be updated

---

## 1. Protocol v4 Schema Changes (from OpenClaw Source)

### 1.1 Connect Params (Client → Gateway)

The `ConnectParamsSchema` in `src/gateway/protocol/schema/frames.ts` defines what the client sends.

**Fields ArayCode ALREADY sends correctly:**
- `minProtocol`: 3
- `maxProtocol`: 4
- `client.id`: `"cli"`
- `client.version`: from `AppConfig.ClientVersion`
- `client.platform`: OS name (`linux`, `windows`, `macos`)
- `client.mode`: `"cli"`
- `client.deviceFamily`: `"desktop"`
- `role`: `"operator"`
- `scopes`: `["operator.read", "operator.write", "operator.approvals", "operator.admin"]`
- `caps`: `["streaming", "stream.text", "agent.stream", "text.stream"]`
- `commands`: `[]`
- `permissions`: `{}`
- `auth.token` / `auth.deviceToken`
- `locale`
- `userAgent`: `"aray-code-cli/{version}"`
- `device` block (id, publicKey, signature, signedAt, nonce)

**NEW v4 Fields ArayCode does NOT send (all optional):**

| Field | Type | Purpose |
|-------|------|---------|
| `client.displayName` | string | Human-readable client name (e.g., "ArayCode CLI") |
| `client.modelIdentifier` | string | Hardware model identifier (e.g., "iPhone17,1") |
| `client.instanceId` | string | Unique instance ID for multi-instance clients |
| `pathEnv` | string | Custom PATH env for node command execution |

**Note:** `GATEWAY_CLIENT_CAPS` in `client-info.ts` defines a new cap:
- `"tool-events"` — indicates the client wants to receive structured tool lifecycle events.

ArayCode does **not** claim this cap. The app currently processes tool calls via `AgentReplyDelta` / `AgentReplyFull` events. If OpenClaw gateways start emitting dedicated `tool-events` frames for clients that claim this cap, ArayCode would miss them.

---

### 1.2 Hello-Ok Response (Gateway → Client)

The `HelloOkSchema` defines what the gateway returns.

**Fields ArayCode CURRENTLY uses:**
- `type` — validated as `"hello-ok"`
- `snapshot` — processed by `SnapshotProcessor` to populate `AgentRegistry`
- `auth.deviceToken` — persisted to config if issued

**Fields ArayCode IGNORES (all part of v4 schema):**

| Field | Type | Purpose | Impact of Ignoring |
|-------|------|---------|-------------------|
| `protocol` | integer | Negotiated protocol version (will be 4) | App doesn't verify what version was actually negotiated |
| `server.version` | string | Gateway version string | Not displayed in status bar |
| `server.connId` | string | Connection ID for debugging | Not logged for diagnostics |
| `features.methods` | string[] | Available gateway RPC methods | App doesn't adapt behavior based on gateway capabilities |
| `features.events` | string[] | Available gateway events | App doesn't verify event support |
| `pluginSurfaceUrls` | Record<string,string> | Scoped plugin URLs (e.g., `canvas`) | Cannot use plugin surfaces like Canvas |
| `auth.role` | string | Negotiated role | Not verified against requested role |
| `auth.scopes` | string[] | Granted scopes | Not verified against requested scopes |
| `auth.issuedAtMs` | integer | Token issue timestamp | Not used for token expiry logic |
| `auth.deviceTokens` | array | Multiple device tokens (bootstrap flow) | Not supported — only single `deviceToken` is read |
| `policy.maxPayload` | integer | Max WebSocket frame size (25 MiB default) | App doesn't enforce send limits |
| `policy.maxBufferedBytes` | integer | Max buffered bytes (50 MiB default) | App doesn't manage backpressure |
| `policy.tickIntervalMs` | integer | Keepalive tick interval (15s default) | App uses hardcoded 30s keepalive instead of negotiated value |

---

### 1.3 New Error Codes

From `connect-error-details.test.ts`, protocol negotiation can fail with:

- `PROTOCOL_MISMATCH` — when `clientMinProtocol > gatewayMaxProtocol` or `clientMaxProtocol < gatewayMinProtocol`
  - Details include: `clientMinProtocol`, `clientMaxProtocol`, `expectedProtocol`, `minimumProbeProtocol`
  - ArayCode's `ValidateHelloOk()` only checks for `type != "hello-ok"` and generic `error` field. It does **not** parse structured error details.

---

## 2. What ArayCode Should Update

### HIGH PRIORITY

#### 2.1 Add `client.displayName` to Connect Params
```csharp
["displayName"] = "ArayCode"
```
Improves identification in gateway logs and `system-presence` listings.

#### 2.2 Read `protocol` from Hello-Ok and Validate
```csharp
var protocol = hello.GetProperty("protocol").GetInt32();
if (protocol < 3 || protocol > 4)
    throw new Exception($"Unsupported protocol version negotiated: {protocol}");
```
Ensures the app actually confirms v4 was negotiated, not just blindly trusts it.

#### 2.3 Read `features.events` and Verify `sessions.messages.subscribe`
ArayCode currently hardcodes the assumption that `sessions.subscribe` / `sessions.messages.subscribe` exist. Protocol v4's `features.events` array tells the client which events the gateway actually supports. The app should verify the events it depends on are present.

#### 2.4 Handle `pluginSurfaceUrls` for Canvas Support
The protocol docs state: *"The experimental Canvas plugin refactor does not support the deprecated `canvasHostUrl`, `canvasCapability`, or `node.canvas.capability.refresh` compatibility path; current native clients and gateways must use plugin surfaces."*

If ArayCode ever wants to support Canvas embedding, it MUST read `hello-ok.pluginSurfaceUrls.canvas` and use that URL. The old `canvasHostUrl` path is deprecated in v4.

#### 2.5 Add `tool-events` to Caps
If/when ArayCode wants structured tool lifecycle events (separate from `AgentReplyDelta`), it should add `"tool-events"` to the `caps` array. Currently the app relies on parsing tool calls from assistant message content blocks.

### MEDIUM PRIORITY

#### 2.6 Read `policy.tickIntervalMs` for Keepalive
```csharp
_ws.Options.KeepAliveInterval = TimeSpan.FromMilliseconds(tickIntervalMs);
```
Instead of the hardcoded 30 seconds, use the gateway-negotiated value.

#### 2.7 Read `server.version` and `server.connId` for Diagnostics
Log these values on connection to help debug gateway compatibility issues.

#### 2.8 Read `auth.scopes` and Verify
Warn the user if requested scopes weren't all granted (e.g., if the gateway admin restricted `operator.admin`).

#### 2.9 Read `auth.deviceTokens` Array (Bootstrap Flow)
The current code only reads `auth.deviceToken` (single string). For QR/bootstrap flows, `hello-ok.auth.deviceTokens` is an array of token objects with role/scope info. ArayCode should support persisting multiple tokens if present.

### LOW PRIORITY

#### 2.10 Add `client.instanceId` for Multi-Instance Support
If users ever run multiple ArayCode instances simultaneously, each should send a unique `instanceId` so the gateway can distinguish them in presence.

#### 2.11 Read `policy.maxPayload` / `policy.maxBufferedBytes`
These are primarily relevant for very large message sends or high-throughput streaming. ArayCode's current use case (text + audio) doesn't hit 25 MiB limits, but reading them makes the client more compliant.

---

## 3. Code Locations to Modify

| File | Action |
|------|--------|
| `GatewayConnectionLifecycle.cs` — `BuildConnectParams()` | Add `client.displayName`, consider `tool-events` cap |
| `GatewayConnectionLifecycle.cs` — `ValidateHelloOk()` | Add `protocol` validation, parse structured error details |
| `GatewayConnectionLifecycle.cs` — `ProcessHelloPayload()` | Read `server`, `features`, `policy`, `pluginSurfaceUrls`, `auth.deviceTokens` |
| `SnapshotProcessor.cs` | Already processes `snapshot` — no change needed |
| `AppConfig.cs` | Add fields for `pluginSurfaceUrls`, `lastNegotiatedProtocol`, `serverVersion` if needed |

---

## 4. Summary Table

| Feature | v3 | v4 | ArayCode Status |
|---------|----|----|-----------------|
| `minProtocol` / `maxProtocol` | ✅ | ✅ | Already sends 3/4 |
| `client.displayName` | ❌ | ✅ Optional | **MISSING** — should add |
| `client.instanceId` | ❌ | ✅ Optional | Missing — low priority |
| `protocol` in hello-ok | ❌ | ✅ Required | **IGNORED** — should validate |
| `server` block | ❌ | ✅ Required | Ignored — medium priority |
| `features` block | ❌ | ✅ Required | Ignored — high priority |
| `pluginSurfaceUrls` | ❌ | ✅ Optional | **IGNORED** — needed for Canvas |
| `auth.deviceTokens` array | ❌ | ✅ Optional | Only reads single token — should handle array |
| `auth.issuedAtMs` | ❌ | ✅ Optional | Ignored |
| `policy` block | ❌ | ✅ Required | Ignored — medium priority |
| `tool-events` cap | ❌ | ✅ | Not claimed — add if needed |
| `PROTOCOL_MISMATCH` error | ❌ | ✅ | Not handled — should add |

---

## 5. Recommended Next Steps

1. **Immediate (1 commit):** Add `client.displayName`, read `protocol` from hello-ok, read `server.version`/`server.connId`, read `policy.tickIntervalMs` and set keepalive from it.
2. **Short-term (1-2 commits):** Read `features.events` and verify required events exist; add `tool-events` cap if implementing structured tool events.
3. **Medium-term:** Handle `pluginSurfaceUrls` for Canvas support; handle `auth.deviceTokens` array for bootstrap flows.
4. **Tests:** Add unit tests for `ValidateHelloOk()` with protocol validation; test `BuildConnectParams()` includes new fields.

---

*Research based on OpenClaw main branch (`src/gateway/protocol/schema/frames.ts`, `client-info.ts`, `connect-error-details.test.ts`) and `docs.openclaw.ai/gateway/protocol`.*

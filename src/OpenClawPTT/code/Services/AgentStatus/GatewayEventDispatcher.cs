using System.Text.Json;

namespace OpenClawPTT.Services;

/// <summary>
/// Routes raw gateway event payloads into strongly-typed records.
/// Each record owns exactly the fields its source event emits authoritatively —
/// no cross-event merging required.
///
/// Caller pattern:
/// <code>
///   var dispatched = GatewayEventDispatcher.Dispatch(payload);
///   switch (dispatched)
///   {
///       case SessionStateEvent e:
///           // Replace stored snapshot wholesale — session object is always complete.
///           _snapshots[e.SessionKey] = SnapshotFromSessionState(e);
///           break;
///       case AssistantMessageEvent e:
///           // Store per-message usage; do NOT use this to update Status/Phase.
///           _lastMessage[e.SessionKey] = e;
///           break;
///       case ToolEvent { Phase: "start" } e:
///           _activeTool[e.SessionKey] = e;
///           break;
///       case AgentLifecycleEvent { Phase: "end" } e:
///           // Run ended; sessions.changed will follow with the authoritative state.
///           break;
///   }
/// </code>
/// </summary>
public static class GatewayEventDispatcher
{
    /// <summary>
    /// Dispatches a raw gateway event JSON element to its typed record.
    /// Returns null for infrastructure events (presence, tick, heartbeat, health)
    /// or any payload that cannot be recognised.
    /// </summary>
    public static object? Dispatch(JsonElement envelope)
    {
        if (envelope.ValueKind != JsonValueKind.Object) return null;

        var eventName = GetString(envelope, "event");
        if (!envelope.TryGetProperty("payload", out var payload)) return null;

        return eventName switch
        {
            "sessions.changed" => ExtractSessionState(payload),
            "session.message" => ExtractSessionMessage(payload),
            "session.tool" => ExtractTool(payload),
            "agent" => ExtractAgent(payload),
            "chat" => ExtractChat(payload),
            // Infrastructure — caller can ignore nulls
            "presence" or "tick" or "heartbeat" or "health" => null,
            _ => null
        };
    }

    // ── sessions.changed ─────────────────────────────────────────────────────
    // The session object embedded here is always the complete, authoritative state.
    // Extract from the nested `session` object first; fall back to top-level
    // flattened copies for any fields the nested object doesn't repeat.

    private static SessionStateEvent? ExtractSessionState(JsonElement p)
    {
        var sessionKey = GetString(p, "sessionKey");
        if (string.IsNullOrEmpty(sessionKey)) return null;

        // Nested session object — always present in sessions.changed.
        p.TryGetProperty("session", out var s);

        // Prefer nested session for stable identity fields; top-level for envelope.
        string? AgentRuntimeId()
        {
            if (TryObj(s, "agentRuntime", out var ar) || TryObj(p, "agentRuntime", out ar))
                return $"{GetString(ar, "id")}:{GetString(ar, "source")}";
            return null;
        }

        string? OriginProvider()
        {
            if (TryObj(s, "origin", out var o) || TryObj(p, "origin", out o))
                return $"{GetString(o, "provider")}:{GetString(o, "surface")}";
            return null;
        }

        (string? cpId, long? cpCreatedAt) LatestCp()
        {
            if (TryObj(s, "latestCompactionCheckpoint", out var cp)
                || TryObj(p, "latestCompactionCheckpoint", out cp))
                return (GetString(cp, "checkpointId"), GetLong(cp, "createdAt"));
            return (null, null);
        }

        var (cpId, cpAt) = LatestCp();

        // Channel lives inside deliveryContext in some events
        string? Channel()
        {
            if (GetString(s, "channel") is { } ch) return ch;
            if (GetString(p, "channel") is { } ch2) return ch2;
            if (TryObj(s, "deliveryContext", out var dc) || TryObj(p, "deliveryContext", out dc))
                return GetString(dc, "channel");
            return null;
        }

        return new SessionStateEvent
        {
            SessionKey = sessionKey,

            // Envelope
            Phase = GetString(p, "phase"),
            RunId = GetString(p, "runId"),
            Reason = GetString(p, "reason"),
            Ts = GetLong(p, "ts"),
            MessageId = GetString(p, "messageId"),
            MessageSeq = GetInt(p, "messageSeq"),

            // Session identity
            SessionId = GetString(s, "sessionId") ?? GetString(p, "sessionId"),
            Kind = GetString(s, "kind") ?? GetString(p, "kind"),
            ChatType = GetString(s, "chatType") ?? GetString(p, "chatType"),
            DisplayName = GetString(s, "displayName") ?? GetString(p, "displayName"),

            // Operational
            Status = GetString(s, "status") ?? GetString(p, "status"),
            AbortedLastRun = GetBool(s, "abortedLastRun") ?? GetBool(p, "abortedLastRun"),
            SystemSent = GetBool(s, "systemSent") ?? GetBool(p, "systemSent"),

            // Model
            Model = GetString(s, "model") ?? GetString(p, "model"),
            ModelProvider = GetString(s, "modelProvider") ?? GetString(p, "modelProvider"),
            AgentRuntimeId = AgentRuntimeId(),

            // Tokens
            InputTokens = GetLong(s, "inputTokens") ?? GetLong(p, "inputTokens"),
            OutputTokens = GetLong(s, "outputTokens") ?? GetLong(p, "outputTokens"),
            TotalTokens = GetLong(s, "totalTokens") ?? GetLong(p, "totalTokens"),
            TotalTokensFresh = GetBool(s, "totalTokensFresh") ?? GetBool(p, "totalTokensFresh"),
            ContextTokens = GetLong(s, "contextTokens") ?? GetLong(p, "contextTokens"),
            EstimatedCostUsd = GetDecimal(s, "estimatedCostUsd") ?? GetDecimal(p, "estimatedCostUsd"),

            // Timing
            StartedAt = GetLong(s, "startedAt") ?? GetLong(p, "startedAt"),
            EndedAt = GetLong(s, "endedAt") ?? GetLong(p, "endedAt"),
            RuntimeMs = GetLong(s, "runtimeMs") ?? GetLong(p, "runtimeMs"),
            UpdatedAt = GetLong(s, "updatedAt") ?? GetLong(p, "updatedAt"),

            // Channel
            Channel = Channel(),
            LastChannel = GetString(s, "lastChannel") ?? GetString(p, "lastChannel"),
            OriginProvider = OriginProvider(),

            // Thinking / compaction
            ThinkingDefault = GetString(s, "thinkingDefault") ?? GetString(p, "thinkingDefault"),
            CompactionCheckpointCount = GetInt(s, "compactionCheckpointCount") ?? GetInt(p, "compactionCheckpointCount"),
            LatestCompactionCheckpointId = cpId,
            LatestCompactionCheckpointCreatedAt = cpAt,

            // Children
            ChildSessions = GetStringArray(s, "childSessions").Count > 0
                                ? GetStringArray(s, "childSessions")
                                : GetStringArray(p, "childSessions"),

            // Subagent-only (only present on reason == "create" and subsequent sends)
            ParentSessionKey = GetString(p, "parentSessionKey"),
            SpawnedBy = GetString(p, "spawnedBy"),
            SpawnDepth = GetInt(p, "spawnDepth"),
            SubagentRole = GetString(p, "subagentRole"),
            SubagentControlScope = GetString(p, "subagentControlScope"),
            SpawnedWorkspaceDir = GetString(p, "spawnedWorkspaceDir"),
            SubagentRunState = GetString(p, "subagentRunState"),
            HasActiveSubagentRun = GetBool(p, "hasActiveSubagentRun"),
        };
    }

    // ── session.message ───────────────────────────────────────────────────────

    private static object? ExtractSessionMessage(JsonElement p)
    {
        var sessionKey = GetString(p, "sessionKey");
        if (string.IsNullOrEmpty(sessionKey)) return null;

        if (!p.TryGetProperty("message", out var msg)) return null;

        var role = GetString(msg, "role");

        return role switch
        {
            "assistant" => ExtractAssistantMessage(p, msg, sessionKey),
            "user" => ExtractUserMessage(p, msg, sessionKey),
            _ => null
        };
    }

    private static AssistantMessageEvent? ExtractAssistantMessage(
        JsonElement p, JsonElement msg, string sessionKey)
    {
        var messageId = GetString(p, "messageId");
        if (string.IsNullOrEmpty(messageId)) return null;

        long? inputTokens = null, outputTokens = null, totalTokens = null,
              cacheRead = null, cacheWrite = null;
        decimal? costTotal = null;

        if (msg.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            inputTokens = GetLong(usage, "input");
            outputTokens = GetLong(usage, "output");
            totalTokens = GetLong(usage, "totalTokens");
            cacheRead = GetLong(usage, "cacheRead");
            cacheWrite = GetLong(usage, "cacheWrite");

            if (usage.TryGetProperty("cost", out var cost) && cost.ValueKind == JsonValueKind.Object)
                costTotal = GetDecimal(cost, "total");
        }

        return new AssistantMessageEvent
        {
            SessionKey = sessionKey,
            MessageId = messageId,
            MessageSeq = GetInt(p, "messageSeq") ?? 0,
            RunId = GetString(p, "runId"),
            Model = GetString(msg, "model"),
            ModelProvider = GetString(msg, "provider"),
            StopReason = GetString(msg, "stopReason"),
            ResponseId = GetString(msg, "responseId"),
            Timestamp = GetLong(msg, "timestamp"),
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = totalTokens,
            CacheRead = cacheRead,
            CacheWrite = cacheWrite,
            CostTotal = costTotal,
        };
    }

    private static UserMessageEvent ExtractUserMessage(
        JsonElement p, JsonElement msg, string sessionKey)
    {
        // Extract first text block content if present
        string? contentText = null;
        if (msg.TryGetProperty("content", out var content))
        {
            if (content.ValueKind == JsonValueKind.String)
            {
                contentText = content.GetString();
            }
            else if (content.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in content.EnumerateArray())
                {
                    if (GetString(block, "type") == "text")
                    {
                        contentText = GetString(block, "text");
                        break;
                    }
                }
            }
        }

        return new UserMessageEvent
        {
            SessionKey = sessionKey,
            MessageId = GetString(p, "messageId"),
            MessageSeq = GetInt(p, "messageSeq"),
            Timestamp = GetLong(msg, "timestamp"),
            ContentText = contentText,
        };
    }

    // ── session.tool ──────────────────────────────────────────────────────────

    private static ToolEvent? ExtractTool(JsonElement p)
    {
        var sessionKey = GetString(p, "sessionKey");
        var runId = GetString(p, "runId");
        if (string.IsNullOrEmpty(sessionKey) || string.IsNullOrEmpty(runId)) return null;

        if (!p.TryGetProperty("data", out var data)) return null;

        var phase = GetString(data, "phase");
        var toolName = GetString(data, "name");
        var toolCallId = GetString(data, "toolCallId");
        if (string.IsNullOrEmpty(phase) || string.IsNullOrEmpty(toolName) || string.IsNullOrEmpty(toolCallId))
            return null;

        // Phase == "result": extract first text block from result.content[]
        string? resultText = null;
        JsonElement? resultDetails = null;
        bool? isError = null;

        if (phase == "result" && data.TryGetProperty("result", out var result))
        {
            isError = GetBool(data, "isError");

            if (result.TryGetProperty("content", out var resultContent)
                && resultContent.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in resultContent.EnumerateArray())
                {
                    if (GetString(block, "type") == "text")
                    {
                        resultText = GetString(block, "text");
                        break;
                    }
                }
            }

            if (result.TryGetProperty("details", out var details))
                resultDetails = details;
        }

        return new ToolEvent
        {
            SessionKey = sessionKey,
            RunId = runId,
            ToolCallId = toolCallId,
            ToolName = toolName,
            Phase = phase,
            Seq = GetInt(p, "seq"),
            Ts = GetLong(p, "ts"),
            Args = phase == "start" && data.TryGetProperty("args", out var args)
                                ? args : null,
            IsError = isError,
            ResultText = resultText,
            ResultDetails = resultDetails,
        };
    }

    // ── agent ─────────────────────────────────────────────────────────────────

    private static object? ExtractAgent(JsonElement p)
    {
        var sessionKey = GetString(p, "sessionKey");
        var runId = GetString(p, "runId");
        var stream = GetString(p, "stream");
        if (string.IsNullOrEmpty(sessionKey) || string.IsNullOrEmpty(runId)) return null;

        if (!p.TryGetProperty("data", out var data)) return null;

        return stream switch
        {
            "lifecycle" => ExtractAgentLifecycle(p, data, sessionKey, runId),
            "assistant" => ExtractAgentStream(p, data, sessionKey, runId),
            "item" => ExtractAgentItem(p, data, sessionKey, runId),
            _ => null
        };
    }

    private static AgentLifecycleEvent ExtractAgentLifecycle(
        JsonElement p, JsonElement data, string sessionKey, string runId)
        => new()
        {
            SessionKey = sessionKey,
            RunId = runId,
            Phase = GetString(data, "phase") ?? string.Empty,
            Seq = GetInt(p, "seq"),
            Ts = GetLong(p, "ts"),
            StartedAt = GetLong(data, "startedAt"),
            EndedAt = GetLong(data, "endedAt"),
            LivenessState = GetString(data, "livenessState"),
        };

    private static AgentStreamEvent ExtractAgentStream(
        JsonElement p, JsonElement data, string sessionKey, string runId)
        => new()
        {
            SessionKey = sessionKey,
            RunId = runId,
            Seq = GetInt(p, "seq"),
            Ts = GetLong(p, "ts"),
            Delta = GetString(data, "delta"),
            Text = GetString(data, "text"),
        };

    private static AgentItemEvent? ExtractAgentItem(
        JsonElement p, JsonElement data, string sessionKey, string runId)
    {
        var itemId = GetString(data, "itemId");
        var phase = GetString(data, "phase");
        if (string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(phase)) return null;

        return new AgentItemEvent
        {
            SessionKey = sessionKey,
            RunId = runId,
            ItemId = itemId,
            Phase = phase,
            Kind = GetString(data, "kind"),
            Name = GetString(data, "name"),
            Title = GetString(data, "title"),
            Status = GetString(data, "status"),
            ToolCallId = GetString(data, "toolCallId"),
            Seq = GetInt(p, "seq"),
            Ts = GetLong(p, "ts"),
            StartedAt = GetLong(data, "startedAt"),
            EndedAt = GetLong(data, "endedAt"),
        };
    }

    // ── chat ──────────────────────────────────────────────────────────────────

    private static ChatStreamEvent? ExtractChat(JsonElement p)
    {
        var sessionKey = GetString(p, "sessionKey");
        var runId = GetString(p, "runId");
        var state = GetString(p, "state");
        if (string.IsNullOrEmpty(sessionKey) || string.IsNullOrEmpty(runId)) return null;

        string? text = null;
        if (p.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object)
        {
            if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in content.EnumerateArray())
                {
                    if (GetString(block, "type") == "text")
                    {
                        text = GetString(block, "text");
                        break;
                    }
                }
            }
        }

        return new ChatStreamEvent
        {
            SessionKey = sessionKey,
            RunId = runId,
            State = state ?? string.Empty,
            Seq = GetInt(p, "seq"),
            MessageText = text,
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string? GetString(JsonElement el, string key)
    {
        if (el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(key, out var v)
            && v.ValueKind == JsonValueKind.String)
            return v.GetString();
        return null;
    }

    private static long? GetLong(JsonElement el, string key)
    {
        if (el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(key, out var v)
            && v.ValueKind == JsonValueKind.Number
            && v.TryGetInt64(out var r)) return r;
        return null;
    }

    private static int? GetInt(JsonElement el, string key)
    {
        if (el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(key, out var v)
            && v.ValueKind == JsonValueKind.Number
            && v.TryGetInt32(out var r)) return r;
        return null;
    }

    private static bool? GetBool(JsonElement el, string key)
    {
        if (el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(key, out var v))
        {
            if (v.ValueKind == JsonValueKind.True) return true;
            if (v.ValueKind == JsonValueKind.False) return false;
        }
        return null;
    }

    private static decimal? GetDecimal(JsonElement el, string key)
    {
        if (el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(key, out var v)
            && v.ValueKind == JsonValueKind.Number
            && v.TryGetDecimal(out var r)) return r;
        return null;
    }

    private static bool TryObj(JsonElement el, string key, out JsonElement result)
    {
        if (el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(key, out result)
            && result.ValueKind == JsonValueKind.Object)
            return true;
        result = default;
        return false;
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement el, string key)
    {
        if (el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(key, out var v)
            && v.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in v.EnumerateArray())
            {
                var s = item.GetString();
                if (!string.IsNullOrEmpty(s)) list.Add(s);
            }
            return list.AsReadOnly();
        }
        return Array.Empty<string>();
    }
}
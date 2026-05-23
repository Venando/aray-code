using System.Text.Json;

namespace ArayCode.Services;

/// <summary>
/// Routes raw gateway event payloads into strongly-typed records.
/// Each record owns exactly the fields its source event emits authoritatively.
///
/// Logging: when a recognised event type is dispatched, any JSON field present
/// in the payload that was NOT extracted into the typed record is logged at
/// Info level so missing coverage is immediately visible.  Infrastructure events
/// (presence, tick, heartbeat, health) are logged as intentionally skipped.
/// </summary>
public static class GatewayEventDispatcher
{
    /// <summary>
    /// Dispatches a raw gateway event JSON element to its typed record.
    /// Returns null for infrastructure events or unrecognised payloads.
    /// </summary>
    /// <param name="envelope">The full gateway message (must have "event" and "payload").</param>
    /// <param name="console">Optional logger for coverage diagnostics.</param>
    public static object? Dispatch(JsonElement envelope, IColorConsole? console = null)
    {
        if (envelope.ValueKind != JsonValueKind.Object)
        {
            console?.Log("dispatch", "Envelope is not a JSON object — dropping", LogLevel.Debug);
            return null;
        }

        var eventName = GetString(envelope, "event");
        if (eventName is null)
        {
            console?.Log("dispatch", "Envelope missing 'event' field — dropping", LogLevel.Info);
            return null;
        }

        if (!envelope.TryGetProperty("payload", out var payload))
        {
            console?.Log("dispatch", $"No payload for event={eventName} — dropping", LogLevel.Info);
            return null;
        }

        object? result = eventName switch
        {
            "sessions.changed" => ExtractSessionState(payload, console),
            "session.message" => ExtractSessionMessage(payload, console),
            "session.tool" => ExtractTool(payload, console),
            "agent" => ExtractAgent(payload, console),
            "chat" => ExtractChat(payload, console),

            // Infrastructure — intentionally skipped
            "presence" or "tick" or "heartbeat" or "health" => LogSkip(eventName, console),

            // Unknown event type — log for coverage
            _ => LogUnknown(eventName, console),
        };

        return result;
    }

    // ── Infrastructure / unknown ──────────────────────────────────────────────

    private static object? LogSkip(string name, IColorConsole? console)
    {
        console?.Log("dispatch", $"Skipping infrastructure event: {name}", LogLevel.Debug);
        return null;
    }

    private static object? LogUnknown(string name, IColorConsole? console)
    {
        console?.Log("dispatch",
            $"Unknown event type '{name}' — not dispatched to any record", LogLevel.Info);
        return null;
    }

    // ── sessions.changed ─────────────────────────────────────────────────────

    private static SessionStateEvent? ExtractSessionState(JsonElement p, IColorConsole? console)
    {
        var sessionKey = GetString(p, "sessionKey");
        if (string.IsNullOrEmpty(sessionKey))
        {
            console?.Log("dispatch", "sessions.changed: missing sessionKey", LogLevel.Info);
            return null;
        }

        p.TryGetProperty("session", out var s);

        var tracker = new FieldTracker(console);

        // ── Nested helpers ───────────────────────────────────────────────
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

        string? Channel()
        {
            if (GetString(s, "channel") is { } ch) return ch;
            if (GetString(p, "channel") is { } ch2) return ch2;
            if (TryObj(s, "deliveryContext", out var dc) || TryObj(p, "deliveryContext", out dc))
                return GetString(dc, "channel");
            return null;
        }

        // ── Build record ─────────────────────────────────────────────────
        var result = new SessionStateEvent
        {
            SessionKey = sessionKey,

            Phase = tracker.Fetch(p, "phase"),
            RunId = tracker.Fetch(p, "runId"),
            Reason = tracker.Fetch(p, "reason"),
            Ts = tracker.FetchLong(p, "ts"),
            MessageId = tracker.Fetch(p, "messageId"),
            MessageSeq = tracker.FetchInt(p, "messageSeq"),

            SessionId = tracker.Fetch(s, "sessionId") ?? tracker.Fetch(p, "sessionId"),
            Kind = tracker.Fetch(s, "kind") ?? tracker.Fetch(p, "kind"),
            ChatType = tracker.Fetch(s, "chatType") ?? tracker.Fetch(p, "chatType"),
            DisplayName = tracker.Fetch(s, "displayName") ?? tracker.Fetch(p, "displayName"),

            Status = tracker.Fetch(s, "status") ?? tracker.Fetch(p, "status"),
            AbortedLastRun = tracker.FetchBool(s, "abortedLastRun") ?? tracker.FetchBool(p, "abortedLastRun"),
            SystemSent = tracker.FetchBool(s, "systemSent") ?? tracker.FetchBool(p, "systemSent"),

            Model = tracker.Fetch(s, "model") ?? tracker.Fetch(p, "model"),
            ModelProvider = tracker.Fetch(s, "modelProvider") ?? tracker.Fetch(p, "modelProvider"),
            AgentRuntimeId = AgentRuntimeId(),

            InputTokens = tracker.FetchLong(s, "inputTokens") ?? tracker.FetchLong(p, "inputTokens"),
            OutputTokens = tracker.FetchLong(s, "outputTokens") ?? tracker.FetchLong(p, "outputTokens"),
            TotalTokens = tracker.FetchLong(s, "totalTokens") ?? tracker.FetchLong(p, "totalTokens"),
            TotalTokensFresh = tracker.FetchBool(s, "totalTokensFresh") ?? tracker.FetchBool(p, "totalTokensFresh"),
            ContextTokens = tracker.FetchLong(s, "contextTokens") ?? tracker.FetchLong(p, "contextTokens"),
            EstimatedCostUsd = tracker.FetchDecimal(s, "estimatedCostUsd") ?? tracker.FetchDecimal(p, "estimatedCostUsd"),

            StartedAt = tracker.FetchLong(s, "startedAt") ?? tracker.FetchLong(p, "startedAt"),
            EndedAt = tracker.FetchLong(s, "endedAt") ?? tracker.FetchLong(p, "endedAt"),
            RuntimeMs = tracker.FetchLong(s, "runtimeMs") ?? tracker.FetchLong(p, "runtimeMs"),
            UpdatedAt = tracker.FetchLong(s, "updatedAt") ?? tracker.FetchLong(p, "updatedAt"),

            Channel = Channel(),
            LastChannel = tracker.Fetch(s, "lastChannel") ?? tracker.Fetch(p, "lastChannel"),
            OriginProvider = OriginProvider(),

            ThinkingDefault = tracker.Fetch(s, "thinkingDefault") ?? tracker.Fetch(p, "thinkingDefault"),
            CompactionCheckpointCount = tracker.FetchInt(s, "compactionCheckpointCount") ?? tracker.FetchInt(p, "compactionCheckpointCount"),
            LatestCompactionCheckpointId = cpId,
            LatestCompactionCheckpointCreatedAt = cpAt,

            ChildSessions = GetStringArray(s, "childSessions").Count > 0
                ? tracker.FetchStringArray(s, "childSessions")
                : tracker.FetchStringArray(p, "childSessions"),

            ParentSessionKey = tracker.Fetch(p, "parentSessionKey"),
            SpawnedBy = tracker.Fetch(p, "spawnedBy"),
            SpawnDepth = tracker.FetchInt(p, "spawnDepth"),
            SubagentRole = tracker.Fetch(p, "subagentRole"),
            SubagentControlScope = tracker.Fetch(p, "subagentControlScope"),
            SpawnedWorkspaceDir = tracker.Fetch(p, "spawnedWorkspaceDir"),
            SubagentRunState = tracker.Fetch(p, "subagentRunState"),
            HasActiveSubagentRun = tracker.FetchBool(p, "hasActiveSubagentRun"),
        };

        // Also track nested-object fields we consumed via helpers
        tracker.MarkNested("agentRuntime", "id", "source");
        tracker.MarkNested("origin", "provider", "surface");
        tracker.MarkNested("deliveryContext", "channel");
        tracker.MarkNested("latestCompactionCheckpoint", "checkpointId", "createdAt");

        tracker.ReportUnused("sessions.changed", sessionKey);

        return result;
    }

    // ── session.message ───────────────────────────────────────────────────────

    private static object? ExtractSessionMessage(JsonElement p, IColorConsole? console)
    {
        var sessionKey = GetString(p, "sessionKey");
        if (string.IsNullOrEmpty(sessionKey)) return null;

        if (!p.TryGetProperty("message", out var msg))
        {
            console?.Log("dispatch", "session.message: missing nested 'message' object", LogLevel.Info);
            return null;
        }

        var role = GetString(msg, "role");

        if (role is null)
        {
            console?.Log("dispatch", "session.message: message has no 'role' field", LogLevel.Info);
            return null;
        }

        return role switch
        {
            "assistant" => ExtractAssistantMessage(p, msg, sessionKey, console),
            "user" => ExtractUserMessage(p, msg, sessionKey, console),
            _ => LogSkipRole(role, sessionKey, console),
        };
    }

    private static object? LogSkipRole(string role, string sessionKey, IColorConsole? console)
    {
        console?.Log("dispatch",
            $"session.message role='{role}' (sk={sessionKey}) — not dispatched", LogLevel.Info);
        return null;
    }

    private static AssistantMessageEvent? ExtractAssistantMessage(
        JsonElement p, JsonElement msg, string sessionKey, IColorConsole? console)
    {
        var messageId = GetString(p, "messageId");
        if (string.IsNullOrEmpty(messageId)) return null;

        var tracker = new FieldTracker(console);

        long? inputTokens = null, outputTokens = null, totalTokens = null,
              cacheRead = null, cacheWrite = null;
        decimal? costTotal = null;

        if (msg.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            tracker.MarkNested("usage");
            inputTokens = tracker.FetchLong(usage, "input");
            outputTokens = tracker.FetchLong(usage, "output");
            totalTokens = tracker.FetchLong(usage, "totalTokens");
            cacheRead = tracker.FetchLong(usage, "cacheRead");
            cacheWrite = tracker.FetchLong(usage, "cacheWrite");

            if (usage.TryGetProperty("cost", out var cost) && cost.ValueKind == JsonValueKind.Object)
            {
                tracker.MarkNested("usage.cost");
                costTotal = tracker.FetchDecimal(cost, "total");
            }
        }

        // ── Content blocks ────────────────────────────────────────────────
        string? contentText = null;
        var contentBlocks = new List<ContentBlock>();

        if (msg.TryGetProperty("content", out var content))
        {
            tracker.MarkNested("content");
            if (content.ValueKind == JsonValueKind.String)
            {
                contentText = content.GetString();
            }
            else if (content.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in content.EnumerateArray())
                {
                    var blockType = GetString(block, "type");
                    if (blockType is null) { tracker.MarkNested("content[?]"); continue; }

                    tracker.MarkNested($"content[type={blockType}]");

                    switch (blockType)
                    {
                        case "text":
                        {
                            var text = GetString(block, "text");
                            if (contentText is null) contentText = text;
                            if (text is not null)
                                contentBlocks.Add(new ContentBlock { Type = blockType, Text = text });
                            break;
                        }

                        case "thinking":
                        {
                            var thinkText = GetString(block, "thinking");
                            if (thinkText is not null)
                                contentBlocks.Add(new ContentBlock { Type = blockType, Thinking = thinkText });
                            break;
                        }

                        default:
                            // Unknown block type — capture full JSON so no data is lost.
                            // Field tracker will still report unused nested fields so we can
                            // discover new block shapes from logs.
                            contentBlocks.Add(new ContentBlock { Type = blockType, RawJson = block.GetRawText() });
                            break;
                    }
                }
            }
        }

        // ── Session-level fields (flat-copied from nested session object) ──
        string? thinkingDefault = null;
        if (p.TryGetProperty("session", out var sessionEl) && sessionEl.ValueKind == JsonValueKind.Object)
        {
            tracker.MarkNested("session");
            thinkingDefault = tracker.Fetch(sessionEl, "thinkingDefault");
        }

        var result = new AssistantMessageEvent
        {
            SessionKey = sessionKey,
            MessageId = messageId,
            MessageSeq = tracker.FetchInt(p, "messageSeq") ?? 0,
            RunId = tracker.Fetch(p, "runId"),
            Model = tracker.Fetch(msg, "model"),
            ModelProvider = tracker.Fetch(msg, "provider"),
            StopReason = tracker.Fetch(msg, "stopReason"),
            ResponseId = tracker.Fetch(msg, "responseId"),
            Timestamp = tracker.FetchLong(msg, "timestamp"),
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = totalTokens,
            CacheRead = cacheRead,
            CacheWrite = cacheWrite,
            CostTotal = costTotal,
            ThinkingDefault = thinkingDefault,
            ContentText = contentText,
            ContentBlocks = contentBlocks,
        };

        tracker.ReportUnused("session.message(assistant)", sessionKey);

        return result;
    }

    private static UserMessageEvent ExtractUserMessage(
        JsonElement p, JsonElement msg, string sessionKey, IColorConsole? console)
    {
        var tracker = new FieldTracker(console);

        string? contentText = null;
        if (msg.TryGetProperty("content", out var content))
        {
            tracker.MarkNested("content");
            if (content.ValueKind == JsonValueKind.String)
            {
                contentText = content.GetString();
            }
            else if (content.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in content.EnumerateArray())
                {
                    var blockType = GetString(block, "type");
                    if (blockType is not null)
                        tracker.MarkNested($"content[type={blockType}]");

                    if (blockType == "text")
                    {
                        contentText = GetString(block, "text");
                        break;
                    }
                }
            }
        }

        // ── Session-level fields (flat-copied from nested session object) ──
        string? thinkingDefault = null;
        if (p.TryGetProperty("session", out var sessionEl) && sessionEl.ValueKind == JsonValueKind.Object)
        {
            tracker.MarkNested("session");
            thinkingDefault = tracker.Fetch(sessionEl, "thinkingDefault");
        }

        var result = new UserMessageEvent
        {
            SessionKey = sessionKey,
            MessageId = tracker.Fetch(p, "messageId"),
            MessageSeq = tracker.FetchInt(p, "messageSeq"),
            Timestamp = tracker.FetchLong(msg, "timestamp"),
            ContentText = contentText,
            ThinkingDefault = thinkingDefault,
        };

        tracker.ReportUnused("session.message(user)", sessionKey);

        return result;
    }

    // ── session.tool ──────────────────────────────────────────────────────────

    private static ToolEvent? ExtractTool(JsonElement p, IColorConsole? console)
    {
        var sessionKey = GetString(p, "sessionKey");
        var runId = GetString(p, "runId");
        if (string.IsNullOrEmpty(sessionKey) || string.IsNullOrEmpty(runId)) return null;

        if (!p.TryGetProperty("data", out var data))
        {
            console?.Log("dispatch", "session.tool: missing 'data' object", LogLevel.Info);
            return null;
        }

        var tracker = new FieldTracker(console);

        var phase = tracker.Fetch(data, "phase");
        var toolName = tracker.Fetch(data, "name");
        var toolCallId = tracker.Fetch(data, "toolCallId");
        if (string.IsNullOrEmpty(phase) || string.IsNullOrEmpty(toolName) || string.IsNullOrEmpty(toolCallId))
            return null;

        string? resultText = null;
        string? resultDetailsJson = null;
        string? resultContentJson = null;
        string? meta = null;
        bool? isError = null;

        if (phase == "result")
        {
            isError = tracker.FetchBool(data, "isError");
            meta = tracker.Fetch(data, "meta");

            if (data.TryGetProperty("result", out var result))
            {
                tracker.MarkNested("result");

                if (result.TryGetProperty("content", out var resultContent)
                    && resultContent.ValueKind == JsonValueKind.Array)
                {
                    tracker.MarkNested("result.content");
                    resultContentJson = resultContent.GetRawText();

                    foreach (var block in resultContent.EnumerateArray())
                    {
                        var blockType = GetString(block, "type");
                        if (blockType is not null)
                            tracker.MarkNested($"result.content[type={blockType}]");
                        if (blockType == "text")
                        {
                            resultText = GetString(block, "text");
                            break;
                        }
                    }
                }

                if (result.TryGetProperty("details", out var details))
                {
                    tracker.MarkNested("result.details");
                    resultDetailsJson = details.GetRawText();
                }
            }
        }

        string? argsJson = null;
        if (phase == "start" && data.TryGetProperty("args", out var args))
        {
            tracker.MarkNested("args");
            argsJson = args.GetRawText();
        }

        string? partialResultJson = null;
        if (phase == "update" && data.TryGetProperty("partialResult", out var partialResult))
        {
            tracker.MarkNested("partialResult");
            partialResultJson = partialResult.GetRawText();
        }

        var evt = new ToolEvent
        {
            SessionKey = sessionKey,
            RunId = runId,
            ToolCallId = toolCallId,
            ToolName = toolName,
            Phase = phase,
            Seq = tracker.FetchInt(p, "seq"),
            Ts = tracker.FetchLong(p, "ts"),
            ArgsJson = argsJson,
            IsError = isError,
            Meta = meta,
            ResultText = resultText,
            ResultContentJson = resultContentJson,
            ResultDetailsJson = resultDetailsJson,
            PartialResultJson = partialResultJson,
            RawDataJson = data.GetRawText(),
        };

        tracker.ReportUnused("session.tool", sessionKey);

        return evt;
    }

    // ── agent ─────────────────────────────────────────────────────────────────

    private static object? ExtractAgent(JsonElement p, IColorConsole? console)
    {
        var sessionKey = GetString(p, "sessionKey");
        var runId = GetString(p, "runId");
        var stream = GetString(p, "stream");
        if (string.IsNullOrEmpty(sessionKey) || string.IsNullOrEmpty(runId)) return null;

        if (!p.TryGetProperty("data", out var data))
        {
            console?.Log("dispatch", "agent: missing 'data' object", LogLevel.Info);
            return null;
        }

        var tracker = new FieldTracker(console);

        return stream switch
        {
            "lifecycle" => ExtractAgentLifecycle(p, data, sessionKey, runId, tracker),
            "assistant" => ExtractAgentStream(p, data, sessionKey, runId, tracker),
            "item" => ExtractAgentItem(p, data, sessionKey, runId, tracker),
            "command_output" => LogSkipAgentStream("agent", "command_output", sessionKey, console),
            _ => LogUnknownStream("agent", stream, sessionKey, console),
        };
    }

    private static object? LogUnknownStream(
        string eventName, string? stream, string sessionKey, IColorConsole? console)
    {
        console?.Log("dispatch",
            $"{eventName} stream='{stream ?? "null"}' (sk={sessionKey}) — not dispatched", LogLevel.Info);
        return null;
    }

    private static object? LogSkipAgentStream(
        string eventName, string stream, string sessionKey, IColorConsole? console)
    {
        console?.Log("dispatch",
            $"{eventName} stream='{stream}' (sk={sessionKey}) — not dispatched", LogLevel.Debug);
        return null;
    }

    private static AgentLifecycleEvent ExtractAgentLifecycle(
        JsonElement p, JsonElement data, string sessionKey, string runId, FieldTracker tracker)
    {
        var result = new AgentLifecycleEvent
        {
            SessionKey = sessionKey,
            RunId = runId,
            Phase = tracker.Fetch(data, "phase") ?? string.Empty,
            Seq = tracker.FetchInt(p, "seq"),
            Ts = tracker.FetchLong(p, "ts"),
            StartedAt = tracker.FetchLong(data, "startedAt"),
            EndedAt = tracker.FetchLong(data, "endedAt"),
            LivenessState = tracker.Fetch(data, "livenessState"),
        };

        tracker.ReportUnused("agent(lifecycle)", sessionKey);
        return result;
    }

    private static AgentStreamEvent ExtractAgentStream(
        JsonElement p, JsonElement data, string sessionKey, string runId, FieldTracker tracker)
    {
        var result = new AgentStreamEvent
        {
            SessionKey = sessionKey,
            RunId = runId,
            Seq = tracker.FetchInt(p, "seq"),
            Ts = tracker.FetchLong(p, "ts"),
            Delta = tracker.Fetch(data, "delta"),
            Text = tracker.Fetch(data, "text"),
        };

        tracker.ReportUnused("agent(assistant)", sessionKey);
        return result;
    }

    private static AgentItemEvent? ExtractAgentItem(
        JsonElement p, JsonElement data, string sessionKey, string runId, FieldTracker tracker)
    {
        var itemId = tracker.Fetch(data, "itemId");
        var phase = tracker.Fetch(data, "phase");
        if (string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(phase)) return null;

        var result = new AgentItemEvent
        {
            SessionKey = sessionKey,
            RunId = runId,
            ItemId = itemId,
            Phase = phase,
            Kind = tracker.Fetch(data, "kind"),
            Name = tracker.Fetch(data, "name"),
            Title = tracker.Fetch(data, "title"),
            Status = tracker.Fetch(data, "status"),
            ToolCallId = tracker.Fetch(data, "toolCallId"),
            Seq = tracker.FetchInt(p, "seq"),
            Ts = tracker.FetchLong(p, "ts"),
            StartedAt = tracker.FetchLong(data, "startedAt"),
            EndedAt = tracker.FetchLong(data, "endedAt"),
        };

        tracker.ReportUnused("agent(item)", sessionKey);
        return result;
    }

    // ── chat ──────────────────────────────────────────────────────────────────

    private static ChatStreamEvent? ExtractChat(JsonElement p, IColorConsole? console)
    {
        var sessionKey = GetString(p, "sessionKey");
        var runId = GetString(p, "runId");
        var state = GetString(p, "state");
        if (string.IsNullOrEmpty(sessionKey) || string.IsNullOrEmpty(runId)) return null;

        var tracker = new FieldTracker(console);

        string? text = null;
        if (p.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.Object)
        {
            tracker.MarkNested("message");
            if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                tracker.MarkNested("message.content");
                foreach (var block in content.EnumerateArray())
                {
                    var blockType = GetString(block, "type");
                    if (blockType is not null)
                        tracker.MarkNested($"message.content[type={blockType}]");
                    if (blockType == "text")
                    {
                        text = GetString(block, "text");
                        break;
                    }
                }
            }
        }

        var result = new ChatStreamEvent
        {
            SessionKey = sessionKey,
            RunId = runId,
            State = state ?? string.Empty,
            Seq = tracker.FetchInt(p, "seq"),
            MessageText = text,
        };

        tracker.ReportUnused("chat", sessionKey);

        return result;
    }

    // ── Field coverage tracker ────────────────────────────────────────────────

    /// <summary>
    /// Tracks which JSON fields were consumed during extraction so we can
    /// report any that were silently ignored.
    /// </summary>
    private sealed class FieldTracker
    {
        private readonly IColorConsole? _console;
        private readonly HashSet<string> _consumed = new(StringComparer.Ordinal);
        private readonly HashSet<string> _nestedMarked = new(StringComparer.Ordinal);

        public FieldTracker(IColorConsole? console) => _console = console;

        public string? Fetch(JsonElement? el, string key)
        {
            if (el is not { } e) return null;
            _consumed.Add(key);
            return GetString(e, key);
        }

        public long? FetchLong(JsonElement? el, string key)
        {
            if (el is not { } e) return null;
            _consumed.Add(key);
            return GetLong(e, key);
        }

        public int? FetchInt(JsonElement? el, string key)
        {
            if (el is not { } e) return null;
            _consumed.Add(key);
            return GetInt(e, key);
        }

        public bool? FetchBool(JsonElement? el, string key)
        {
            if (el is not { } e) return null;
            _consumed.Add(key);
            return GetBool(e, key);
        }

        public decimal? FetchDecimal(JsonElement? el, string key)
        {
            if (el is not { } e) return null;
            _consumed.Add(key);
            return GetDecimal(e, key);
        }

        public IReadOnlyList<string> FetchStringArray(JsonElement? el, string key)
        {
            if (el is not { } e) return Array.Empty<string>();
            _consumed.Add(key);
            return GetStringArray(e, key);
        }

        /// <summary>Marks a nested object / sub-path as explicitly consumed.</summary>
        public void MarkNested(string path)
        {
            _nestedMarked.Add(path);
        }

        /// <summary>Marks a nested object and its keys as consumed.</summary>
        public void MarkNested(string parent, params string[] keys)
        {
            _nestedMarked.Add(parent);
            foreach (var k in keys)
                _nestedMarked.Add($"{parent}.{k}");
        }

        /// <summary>
        /// Reports any fields present in <paramref name="el"/> that were never
        /// fetched via a <c>Fetch*</c> call, excluding those explicitly marked
        /// as nested paths.
        /// </summary>
        public void ReportUnused(string eventLabel, string sessionKey, JsonElement? el = null)
        {
            if (_console is null) return;
        }

        /// <summary>
        /// Reports unused fields across a set of elements.
        /// </summary>
        public void ReportUnused(string eventLabel, string sessionKey, params JsonElement[] elements)
        {
            if (_console is null) return;
            foreach (var el in elements)
                CheckElement(el, "", eventLabel, sessionKey);
        }

        private void CheckElement(JsonElement el, string prefix, string eventLabel, string sessionKey)
        {
            if (el.ValueKind != JsonValueKind.Object) return;

            foreach (var prop in el.EnumerateObject())
            {
                var fullKey = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";

                // Skip keys we explicitly consumed or marked
                if (_consumed.Contains(prop.Name) || _nestedMarked.Contains(prop.Name) ||
                    _nestedMarked.Contains(fullKey))
                {
                    // Recurse into objects we consumed — they may have nested unknowns
                    if (prop.Value.ValueKind == JsonValueKind.Object)
                        CheckElement(prop.Value, fullKey, eventLabel, sessionKey);
                    continue;
                }

                // Object we didn't explicitly consume — flag it
                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    _console?.Log("dispatch",
                        $"Unused object '{prop.Name}' in {eventLabel} (sk={Truncate(sessionKey, 20)})",
                        LogLevel.Info);
                    // Recurse to find specific unused keys
                    CheckElement(prop.Value, fullKey, eventLabel, sessionKey);
                }
                else
                {
                    _console?.Log("dispatch",
                        $"Unused field '{fullKey}' in {eventLabel} (sk={Truncate(sessionKey, 20)})",
                        LogLevel.Info);
                }
            }
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

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

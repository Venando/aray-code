using System.Text.Json;

namespace OpenClawPTT.Services;

/// <summary>
/// Parses chat history messages into <see cref="HistoryMessageEvent"/> records.
/// Schema matches <see cref="UserMessageHelper"/> (role, content blocks, usage, etc.).
///
/// Field coverage is tracked — any JSON field present in the message that is not
/// extracted into the record is logged at Info level.
/// </summary>
public static class HistoryMessageParser
{
    /// <summary>
    /// Extracts the most recent N messages from a history array and stores
    /// them as typed events in the activity store for bottom-panel display.
    /// </summary>
    public static void ExtractRecent(
        JsonElement messages,
        string sessionKey,
        IAgentActivityStore store,
        IColorConsole? console = null,
        int count = 2)
    {
        if (messages.ValueKind != JsonValueKind.Array || string.IsNullOrEmpty(sessionKey))
            return;

        int total = messages.GetArrayLength();
        int start = Math.Max(0, total - count);

        for (int i = start; i < total; i++)
        {
            var evt = Extract(messages[i], sessionKey, console);
            if (evt is not null)
                store.Store(evt);
        }
    }

    /// <summary>
    /// Extracts a single history message into a typed record.
    /// Returns null when role is missing or not assistant/user.
    /// Logs unused fields at Info level.
    /// </summary>
    public static HistoryMessageEvent? Extract(
        JsonElement msg,
        string sessionKey,
        IColorConsole? console = null)
    {
        if (msg.ValueKind != JsonValueKind.Object) return null;

        var tracker = new FieldTracker(console);

        var role = tracker.Fetch(msg, "role");
        if (role is null || role is not ("assistant" or "user"))
        {
            if (role is not null)
                console?.Log("history", $"Skipping history message role='{role}'", LogLevel.Info);
            return null;
        }

        // ── Envelope ─────────────────────────────────────────────────────
        var provider = tracker.Fetch(msg, "provider");
        var model = tracker.Fetch(msg, "model");
        var stopReason = tracker.Fetch(msg, "stopReason");
        var timestamp = tracker.FetchLong(msg, "timestamp");
        var responseId = tracker.Fetch(msg, "responseId");
        var api = tracker.Fetch(msg, "api");

        // ── Usage ─────────────────────────────────────────────────────────
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
        var toolCalls = new List<HistoryToolCall>();
        var thinkingBlocks = new List<string>();

        if (msg.TryGetProperty("content", out var content))
        {
            if (content.ValueKind == JsonValueKind.String)
            {
                tracker.MarkNested("content");
                contentText = content.GetString();
            }
            else if (content.ValueKind == JsonValueKind.Array)
            {
                tracker.MarkNested("content");
                foreach (var block in content.EnumerateArray())
                {
                    var blockType = tracker.Fetch(block, "type");
                    if (blockType is null) { tracker.MarkNested("content[?]"); continue; }

                    tracker.MarkNested($"content[type={blockType}]");

                    switch (blockType)
                    {
                        case "text":
                            contentText = tracker.Fetch(block, "text") ?? contentText;
                            break;

                        case "toolCall":
                        case "tool_use":
                        {
                            var tcId = tracker.Fetch(block, "id");
                            var tcName = tracker.Fetch(block, "name");
                            var tcArgs = block.TryGetProperty("arguments", out var a)
                                ? a.GetRawText() : null;
                            tracker.MarkNested($"content[{blockType}].arguments");

                            if (tcId is not null && tcName is not null)
                                toolCalls.Add(new HistoryToolCall { Id = tcId, Name = tcName, ArgumentsJson = tcArgs });
                            break;
                        }

                        case "thinking":
                        {
                            var thinkText = tracker.Fetch(block, "thinking");
                            if (!string.IsNullOrEmpty(thinkText))
                                thinkingBlocks.Add(thinkText);
                            break;
                        }
                    }
                }
            }
        }

        // ── Sender metadata (untrusted) ───────────────────────────────────
        string? senderLabel = null;
        string? senderId = null;
        if (msg.TryGetProperty("sender", out var sender) && sender.ValueKind == JsonValueKind.Object)
        {
            tracker.MarkNested("sender");
            senderLabel = tracker.Fetch(sender, "label");
            senderId = tracker.Fetch(sender, "id");
        }

        // ── __openclaw metadata ───────────────────────────────────────────
        string? openClawId = null;
        int? openClawSeq = null;
        if (msg.TryGetProperty("__openclaw", out var oc) && oc.ValueKind == JsonValueKind.Object)
        {
            tracker.MarkNested("__openclaw");
            openClawId = tracker.Fetch(oc, "id");
            openClawSeq = tracker.FetchInt(oc, "seq");
        }

        var result = new HistoryMessageEvent
        {
            SessionKey = sessionKey,
            Role = role,
            Provider = provider,
            Model = model,
            StopReason = stopReason,
            Timestamp = timestamp,
            ResponseId = responseId,
            Api = api,
            SenderLabel = senderLabel,
            SenderId = senderId,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = totalTokens,
            CacheRead = cacheRead,
            CacheWrite = cacheWrite,
            CostTotal = costTotal,
            ContentText = contentText,
            ToolCalls = toolCalls.AsReadOnly(),
            ThinkingBlocks = thinkingBlocks.AsReadOnly(),
            OpenClawId = openClawId,
            OpenClawSeq = openClawSeq,
        };

        tracker.ReportUnused("history", sessionKey, msg);

        return result;
    }

    // ── Field tracker (same pattern as GatewayEventDispatcher) ────────────

    private sealed class FieldTracker
    {
        private readonly IColorConsole? _console;
        private readonly HashSet<string> _consumed = new(StringComparer.Ordinal);
        private readonly HashSet<string> _nestedMarked = new(StringComparer.Ordinal);

        public FieldTracker(IColorConsole? console) => _console = console;

        public string? Fetch(JsonElement el, string key)
        {
            _consumed.Add(key);
            return GetString(el, key);
        }

        public long? FetchLong(JsonElement el, string key)
        {
            _consumed.Add(key);
            return GetLong(el, key);
        }

        public int? FetchInt(JsonElement el, string key)
        {
            _consumed.Add(key);
            return GetInt(el, key);
        }

        public decimal? FetchDecimal(JsonElement el, string key)
        {
            _consumed.Add(key);
            return GetDecimal(el, key);
        }

        public void MarkNested(string path) => _nestedMarked.Add(path);

        public void ReportUnused(string label, string sessionKey, JsonElement el)
        {
            if (_console is null) return;
            CheckElement(el, "", label, sessionKey);
        }

        private void CheckElement(JsonElement el, string prefix, string label, string sessionKey)
        {
            if (el.ValueKind != JsonValueKind.Object) return;

            foreach (var prop in el.EnumerateObject())
            {
                var fullKey = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                if (_consumed.Contains(prop.Name) || _nestedMarked.Contains(prop.Name) || _nestedMarked.Contains(fullKey))
                {
                    if (prop.Value.ValueKind == JsonValueKind.Object)
                        CheckElement(prop.Value, fullKey, label, sessionKey);
                    continue;
                }

                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    _console.Log("history", $"Unused object '{prop.Name}' in {label} (sk={Truncate(sessionKey, 20)})", LogLevel.Info);
                    CheckElement(prop.Value, fullKey, label, sessionKey);
                }
                else
                {
                    _console.Log("history", $"Unused field '{fullKey}' in {label} (sk={Truncate(sessionKey, 20)})", LogLevel.Info);
                }
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

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

    private static decimal? GetDecimal(JsonElement el, string key)
    {
        if (el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(key, out var v)
            && v.ValueKind == JsonValueKind.Number
            && v.TryGetDecimal(out var r)) return r;
        return null;
    }
}

using System.Text.Json;

namespace OpenClawPTT.Services;

/// <summary>
/// Parses chat history messages into typed activity events for
/// storage in <see cref="IAgentActivityStore"/>.
///
/// History messages use a different JSON schema than live gateway events
/// (see <see cref="UserMessageHelper"/> for the schema).  This parser
/// extracts tool calls and message metadata from history so the bottom
/// panel can show last-action descriptions for agents loaded from history.
/// </summary>
public static class HistoryMessageParser
{
    /// <summary>
    /// Extracts the last N history messages for a session and stores
    /// them as typed events in the activity store.  This lets
    /// <see cref="IAgentActivityStore.GetLastActionDescription"/>
    /// return meaningful descriptions for agents that were loaded from
    /// history rather than live wire events.
    /// </summary>
    /// <param name="messages">The messages array from chat.history / sessions.preview.</param>
    /// <param name="sessionKey">Session key these messages belong to.</param>
    /// <param name="store">Target activity store.</param>
    /// <param name="count">How many recent messages to extract (default 2).</param>
    public static void ExtractRecent(
        JsonElement messages,
        string sessionKey,
        IAgentActivityStore store,
        int count = 2)
    {
        if (messages.ValueKind != JsonValueKind.Array)
            return;

        if (string.IsNullOrEmpty(sessionKey))
            return;

        int total = messages.GetArrayLength();
        int start = Math.Max(0, total - count);

        for (int i = start; i < total; i++)
        {
            var msg = messages[i];
            if (msg.ValueKind != JsonValueKind.Object)
                continue;

            var role = GetString(msg, "role");
            if (role is null) continue;

            long? timestamp = null;
            if (msg.TryGetProperty("timestamp", out var ts) && ts.TryGetInt64(out var t))
                timestamp = t;

            // ── Always store basic session state (model, provider, tokens) ──
            var state = AgentStatusExtractor.FromHistoryMessage(msg, sessionKey);
            if (state is not null)
                store.Store(state);

            // ── Extract tool calls from content blocks ────────────────────
            if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                int callIdx = 0;
                foreach (var block in content.EnumerateArray())
                {
                    var blockType = GetString(block, "type");
                    if (blockType is "toolCall" or "tool_use")
                    {
                        var toolName = GetString(block, "name");
                        var argsEl = block.TryGetProperty("arguments", out var a)
                            ? a.GetRawText() : null;

                        if (toolName is not null)
                        {
                            store.Store(new ToolEvent
                            {
                                SessionKey = sessionKey,
                                RunId = "history",
                                ToolCallId = $"history_{i}_{callIdx}",
                                ToolName = toolName,
                                Phase = "start",
                                Ts = timestamp,
                                ArgsJson = argsEl,
                            });
                            callIdx++;
                        }
                    }
                }
            }

            // ── Assistant message events for non-toolUse messages ────────
            if (role == "assistant")
            {
                var stopReason = GetString(msg, "stopReason");
                store.Store(new AssistantMessageEvent
                {
                    SessionKey = sessionKey,
                    MessageId = $"history_{i}",
                    MessageSeq = i,
                    Timestamp = timestamp,
                    StopReason = stopReason,
                });
            }

            // ── User messages ─────────────────────────────────────────────
            if (role == "user")
            {
                string? contentText = null;
                if (msg.TryGetProperty("content", out var uc) && uc.ValueKind == JsonValueKind.String)
                    contentText = uc.GetString();

                store.Store(new UserMessageEvent
                {
                    SessionKey = sessionKey,
                    MessageId = $"history_{i}",
                    MessageSeq = i,
                    Timestamp = timestamp,
                    ContentText = contentText,
                });
            }
        }
    }

    private static string? GetString(JsonElement el, string key)
    {
        if (el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(key, out var v)
            && v.ValueKind == JsonValueKind.String)
            return v.GetString();
        return null;
    }
}

using OpenClawPTT.Services.Themes;

namespace OpenClawPTT.Services;

/// <summary>
/// Thread-safe per-agent store that holds gateway events organised by type.
///
/// Design:
///   - Each <c>Store(T)</c> overload appends the event to the right collection.
///   - <c>SessionStateEvent</c> replaces the stored state wholesale.
///   - Query methods pool from collections to answer "last X" questions.
///   - No field-level merging — each event type is authoritative for its fields.
/// </summary>
public sealed class AgentActivityStore : IAgentActivityStore
{
    private readonly Dictionary<string, SessionRecord> _sessions = new();
    private readonly object _lock = new();

    public event Action<string>? Changed;

    // ── Per-session record ─────────────────────────────────────────────────

    private sealed class SessionRecord
    {
        public SessionStateEvent? State;
        public readonly List<AssistantMessageEvent> AssistantMessages = new();
        public readonly List<ToolEvent> ToolCalls = new();
        public readonly List<UserMessageEvent> UserMessages = new();
        public readonly List<AgentLifecycleEvent> Lifecycles = new();
        public readonly List<AgentItemEvent> Items = new();
        public readonly List<HistoryMessageEvent> HistoryMessages = new();
    }

    private SessionRecord GetOrCreate(string sessionKey)
    {
        if (!_sessions.TryGetValue(sessionKey, out var rec))
        {
            rec = new SessionRecord();
            _sessions[sessionKey] = rec;
        }
        return rec;
    }

    private SessionRecord? Get(string sessionKey)
    {
        _sessions.TryGetValue(sessionKey, out var rec);
        return rec;
    }

    // ── IAgentActivityStore — queries ──────────────────────────────────────

    public SessionStateEvent? GetSessionState(string sessionKey)
    {
        lock (_lock) return Get(sessionKey)?.State;
    }

    public IReadOnlyList<string> GetTrackedSessions()
    {
        lock (_lock) return _sessions.Keys.ToList().AsReadOnly();
    }

    public IReadOnlyList<string> GetSubagents(string parentSessionKey)
    {
        lock (_lock)
        {
            var list = new List<string>();
            foreach (var (key, rec) in _sessions)
            {
                if (rec.State?.ParentSessionKey == parentSessionKey
                    || rec.State?.SpawnedBy == parentSessionKey)
                {
                    list.Add(key);
                }
            }
            return list.AsReadOnly();
        }
    }

    public string? GetModel(string sessionKey)
    {
        lock (_lock)
        {
            var st = Get(sessionKey)?.State;
            return st?.Model;
        }
    }

    public AssistantMessageEvent? GetLastAssistantMessage(string sessionKey)
    {
        lock (_lock)
        {
            var rec = Get(sessionKey);
            if (rec is null || rec.AssistantMessages.Count == 0) return null;
            return rec.AssistantMessages[^1];
        }
    }

    public ToolEvent? GetLastToolCall(string sessionKey)
    {
        lock (_lock)
        {
            var rec = Get(sessionKey);
            if (rec is null || rec.ToolCalls.Count == 0) return null;
            return rec.ToolCalls[^1];
        }
    }

    public UserMessageEvent? GetLastUserMessage(string sessionKey)
    {
        lock (_lock)
        {
            var rec = Get(sessionKey);
            if (rec is null || rec.UserMessages.Count == 0) return null;
            return rec.UserMessages[^1];
        }
    }

    public AgentLifecycleEvent? GetLastLifecycle(string sessionKey)
    {
        lock (_lock)
        {
            var rec = Get(sessionKey);
            if (rec is null || rec.Lifecycles.Count == 0) return null;
            return rec.Lifecycles[^1];
        }
    }

    public HistoryMessageEvent? GetLastHistoryMessage(string sessionKey)
    {
        lock (_lock)
        {
            var rec = Get(sessionKey);
            if (rec is null || rec.HistoryMessages.Count == 0) return null;
            return rec.HistoryMessages[^1];
        }
    }

    public long? GetLastActivityTime(string sessionKey)
    {
        lock (_lock)
        {
            var rec = Get(sessionKey);
            if (rec is null) return null;

            long? best = null;
            void Consider(long? val) { if (val is { } v && (best is null || v > best)) best = v; }

            Consider(rec.State?.EndedAt);
            Consider(rec.State?.UpdatedAt);
            Consider(rec.State?.Ts);

            if (rec.HistoryMessages.Count > 0)
                Consider(rec.HistoryMessages[^1].Timestamp);
            if (rec.AssistantMessages.Count > 0)
                Consider(rec.AssistantMessages[^1].Timestamp);
            if (rec.ToolCalls.Count > 0)
                Consider(rec.ToolCalls[^1].Ts);
            if (rec.UserMessages.Count > 0)
                Consider(rec.UserMessages[^1].Timestamp);
            if (rec.Lifecycles.Count > 0)
            {
                var lc = rec.Lifecycles[^1];
                Consider(lc.EndedAt);
                Consider(lc.Ts);
            }

            return best;
        }
    }

    public string? GetActivityType(string sessionKey)
    {
        lock (_lock)
        {
            var rec = Get(sessionKey);
            if (rec is null) return null;

            var lastTool = rec.ToolCalls.Count > 0 ? rec.ToolCalls[^1] : null;
            var lastMsg = rec.AssistantMessages.Count > 0 ? rec.AssistantMessages[^1] : null;
            var lastUser = rec.UserMessages.Count > 0 ? rec.UserMessages[^1] : null;

            long? toolTime = lastTool?.Ts;
            long? msgTime = lastMsg?.Timestamp;
            long? userTime = lastUser?.Timestamp;

            if (toolTime is { } tt && (msgTime is null || tt >= msgTime) && (userTime is null || tt >= userTime))
                return "tool";
            if (msgTime is not null)
                return "message";
            if (userTime is not null)
                return "user";
            return null;
        }
    }

    public string GetStatusEmoji(string sessionKey)
    {
        lock (_lock)
        {
            var st = Get(sessionKey)?.State;
            if (st is null) return AgentStatusEmoji.Unknown;

            // Subagent states
            if (st.ParentSessionKey is not null || st.SpawnedBy is not null)
            {
                if (st.SubagentRunState == "historical")
                    return AgentStatusEmoji.Finished;
                if (st.HasActiveSubagentRun == true)
                    return AgentStatusEmoji.ToolExecuting;
                if (st.Status == "running")
                    return AgentStatusEmoji.Spawning;
                return AgentStatusEmoji.UnknownSubagent;
            }

            // Main agent
            if (st.AbortedLastRun == true)
                return AgentStatusEmoji.Aborted;
            if (st.Status == "running" && st.ChildSessions.Count > 0)
                return AgentStatusEmoji.Yielding;

            // Default: ready
            return AgentStatusEmoji.Ready;
        }
    }

    // ── IAgentActivityStore — mutations ────────────────────────────────────

    public void Store(SessionStateEvent e)
    {
        if (string.IsNullOrEmpty(e.SessionKey)) return;

        lock (_lock)
        {
            var rec = GetOrCreate(e.SessionKey);
            rec.State = e; // Replace wholesale
        }
        Changed?.Invoke(e.SessionKey);
    }

    public void Store(AssistantMessageEvent e)
    {
        if (string.IsNullOrEmpty(e.SessionKey)) return;

        lock (_lock)
        {
            var rec = GetOrCreate(e.SessionKey);
            rec.AssistantMessages.Add(e);
        }
        Changed?.Invoke(e.SessionKey);
    }

    public void Store(ToolEvent e)
    {
        if (string.IsNullOrEmpty(e.SessionKey)) return;

        lock (_lock)
        {
            var rec = GetOrCreate(e.SessionKey);
            rec.ToolCalls.Add(e);
        }
        Changed?.Invoke(e.SessionKey);
    }

    public void Store(UserMessageEvent e)
    {
        if (string.IsNullOrEmpty(e.SessionKey)) return;

        lock (_lock)
        {
            var rec = GetOrCreate(e.SessionKey);
            rec.UserMessages.Add(e);
        }
        Changed?.Invoke(e.SessionKey);
    }

    public void Store(AgentLifecycleEvent e)
    {
        if (string.IsNullOrEmpty(e.SessionKey)) return;

        lock (_lock)
        {
            var rec = GetOrCreate(e.SessionKey);
            rec.Lifecycles.Add(e);
        }
        Changed?.Invoke(e.SessionKey);
    }

    public void Store(AgentItemEvent e)
    {
        if (string.IsNullOrEmpty(e.SessionKey)) return;

        lock (_lock)
        {
            var rec = GetOrCreate(e.SessionKey);
            rec.Items.Add(e);
        }
        // Items are noisy — don't fire Changed for every item
    }

    public void Store(HistoryMessageEvent e)
    {
        if (string.IsNullOrEmpty(e.SessionKey)) return;

        lock (_lock)
        {
            var rec = GetOrCreate(e.SessionKey);
            rec.HistoryMessages.Add(e);
        }
        Changed?.Invoke(e.SessionKey);
    }

    public void Remove(string sessionKey)
    {
        lock (_lock)
        {
            _sessions.Remove(sessionKey);
        }
        Changed?.Invoke(sessionKey);
    }

    public void Reset(string sessionKey)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionKey, out var rec))
            {
                // Keep the record but clear operational state
                rec.State = rec.State is { } st
                    ? st with
                    {
                        Status = null,
                        InputTokens = null,
                        OutputTokens = null,
                        TotalTokens = null,
                        ContextTokens = null,
                        EstimatedCostUsd = null,
                        RuntimeMs = null,
                        EndedAt = null,
                        ChildSessions = Array.Empty<string>(),
                        AbortedLastRun = null,
                    }
                    : null;
                rec.AssistantMessages.Clear();
                rec.ToolCalls.Clear();
                rec.UserMessages.Clear();
                rec.Lifecycles.Clear();
                rec.Items.Clear();
                rec.HistoryMessages.Clear();
            }
        }
        Changed?.Invoke(sessionKey);
    }
}

/// <summary>
/// Emoji constants shared between <see cref="AgentActivityStore"/>
/// and <see cref="AgentStatusBottomPanel"/>.
/// </summary>
internal static class AgentStatusEmoji
{
    public static string Ready => $"[{ThemeProvider.Current.Tools.Messages.Success}]•[/]";
    public const string Aborted = "▶";
    public const string ToolExecuting = "▶";
    public static string Finished => $"[{ThemeProvider.Current.Tools.Messages.Success}]•[/]";
    public const string Spawning = "▶";
    public const string UnknownSubagent = "◘";
    public const string Yielding = "▶";
    public const string Unknown = "•";
}

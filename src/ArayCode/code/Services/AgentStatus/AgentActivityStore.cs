using ArayCode.Services.Themes;

namespace ArayCode.Services;

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
        public const int MaxRecords = 5;

        public readonly List<SessionStateEvent> States = new();
        public readonly List<AssistantMessageEvent> AssistantMessages = new();
        public readonly List<ToolEvent> ToolCalls = new();
        public readonly List<UserMessageEvent> UserMessages = new();
        public readonly List<AgentLifecycleEvent> Lifecycles = new();
        public readonly List<AgentItemEvent> Items = new();
        public readonly List<HistoryMessageEvent> HistoryMessages = new();

        /// <summary>Add an item and trim to <see cref="MaxRecords"/>.</summary>
        public static void AddAndTrim<T>(List<T> list, T item)
        {
            list.Add(item);
            if (list.Count > MaxRecords)
                list.RemoveRange(0, list.Count - MaxRecords);
        }
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
        lock (_lock)
        {
            var rec = Get(sessionKey);
            return rec is not null && rec.States.Count > 0 ? rec.States[^1] : null;
        }
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
                var st = rec.States.Count > 0 ? rec.States[^1] : null;
                if (st?.ParentSessionKey == parentSessionKey
                    || st?.SpawnedBy == parentSessionKey)
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
            var rec = Get(sessionKey);
            var st = rec is not null && rec.States.Count > 0 ? rec.States[^1] : null;
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

            if (rec.States.Count > 0)
            {
                var st = rec.States[^1];
                Consider(st.EndedAt);
                Consider(st.UpdatedAt);
                Consider(st.Ts);
            }

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

    // TODO: If tool has "result" something something ignore it?
    public TResult? SelectLatestActivity<TResult>(
        string sessionKey,
        Func<HistoryMessageEvent, TResult>? onHistory = null,
        Func<ToolEvent, TResult>? onTool = null,
        Func<AssistantMessageEvent, TResult>? onAssistant = null,
        Func<UserMessageEvent, TResult>? onUser = null,
        Func<SessionStateEvent, TResult>? onState = null)
    {
        HistoryMessageEvent? hist = null;
        ToolEvent? tool = null;
        AssistantMessageEvent? msg = null;
        UserMessageEvent? user = null;
        SessionStateEvent? state = null;

        long? histTs = null, toolTs = null, msgTs = null, userTs = null, stateTs = null;

        lock (_lock)
        {
            var rec = Get(sessionKey);
            if (rec is null) return default;

            if (onHistory is not null && rec.HistoryMessages.Count > 0)
            {
                hist = rec.HistoryMessages[^1];
                histTs = hist.Timestamp;
            }

            if (onTool is not null && rec.ToolCalls.Count > 0)
            {
                tool = rec.ToolCalls[^1];
                toolTs = tool.Ts;
            }

            if (onAssistant is not null)
            {
                // Walk back to skip assistant messages with no text content
                for (int i = rec.AssistantMessages.Count - 1; i >= 0; i--)
                {
                    if (!string.IsNullOrWhiteSpace(rec.AssistantMessages[i].ContentText))
                    {
                        msg = rec.AssistantMessages[i];
                        msgTs = msg.Timestamp;
                        break;
                    }
                }
            }

            if (onUser is not null && rec.UserMessages.Count > 0)
            {
                user = rec.UserMessages[^1];
                userTs = user.Timestamp;
            }

            if (onState is not null && rec.States.Count > 0)
            {
                state = rec.States[^1];
                stateTs = state.UpdatedAt ?? state.Ts;
            }
        }

        // Find the best timestamp among the event types we actually queried
        long best = long.MinValue;
        bool any = false;

        if (histTs  is { } ht) { best = Math.Max(best, ht); any = true; }
        if (toolTs  is { } tt) { best = Math.Max(best, tt); any = true; }
        if (msgTs   is { } mt) { best = Math.Max(best, mt); any = true; }
        if (userTs  is { } ut) { best = Math.Max(best, ut); any = true; }
        if (stateTs is { } st) { best = Math.Max(best, st); any = true; }

        if (!any) return default;

        if (histTs == best)  return onHistory!(hist!);
        if (toolTs == best)  return onTool!(tool!);
        if (userTs == best)  return onUser!(user!);
        if (stateTs == best) return onState!(state!);
        return onAssistant!(msg!);
    }

    public string GetStatusEmoji(string sessionKey) =>
        SelectLatestActivity(
            sessionKey,
            onHistory:   entry => entry.StopReason == "stop" ? AgentStatusEmoji.Ready : ((entry.StopReason == "aborted") ? AgentStatusEmoji.Aborted : AgentStatusEmoji.ToolExecuting),
            onTool:      entry => AgentStatusEmoji.ToolExecuting,
            onAssistant: entry => entry.StopReason == "stop" ? AgentStatusEmoji.Ready : ((entry.StopReason == "aborted") ? AgentStatusEmoji.Aborted : AgentStatusEmoji.ToolExecuting),
            onUser: entry => AgentStatusEmoji.ToolExecuting)
        ?? AgentStatusEmoji.Unknown;

    // ── IAgentActivityStore — mutations ────────────────────────────────────

    public void Store(SessionStateEvent e)
    {
        if (string.IsNullOrEmpty(e.SessionKey)) return;

        lock (_lock)
        {
            var rec = GetOrCreate(e.SessionKey);
            SessionRecord.AddAndTrim(rec.States, e);
        }
        Changed?.Invoke(e.SessionKey);
    }

    public void Store(AssistantMessageEvent e)
    {
        if (string.IsNullOrEmpty(e.SessionKey)) return;

        lock (_lock)
        {
            var rec = GetOrCreate(e.SessionKey);
            SessionRecord.AddAndTrim(rec.AssistantMessages, e);
        }
        Changed?.Invoke(e.SessionKey);
    }

    public void Store(ToolEvent e)
    {
        if (string.IsNullOrEmpty(e.SessionKey)) return;

        lock (_lock)
        {
            var rec = GetOrCreate(e.SessionKey);
            SessionRecord.AddAndTrim(rec.ToolCalls, e);
        }
        Changed?.Invoke(e.SessionKey);
    }

    public void Store(UserMessageEvent e)
    {
        if (string.IsNullOrEmpty(e.SessionKey)) return;

        lock (_lock)
        {
            var rec = GetOrCreate(e.SessionKey);
            SessionRecord.AddAndTrim(rec.UserMessages, e);
        }
        Changed?.Invoke(e.SessionKey);
    }

    public void Store(AgentLifecycleEvent e)
    {
        if (string.IsNullOrEmpty(e.SessionKey)) return;

        lock (_lock)
        {
            var rec = GetOrCreate(e.SessionKey);
            SessionRecord.AddAndTrim(rec.Lifecycles, e);
        }
        Changed?.Invoke(e.SessionKey);
    }

    public void Store(AgentItemEvent e)
    {
        if (string.IsNullOrEmpty(e.SessionKey)) return;

        lock (_lock)
        {
            var rec = GetOrCreate(e.SessionKey);
            SessionRecord.AddAndTrim(rec.Items, e);
        }
        // Items are noisy — don't fire Changed for every item
    }

    public void Store(HistoryMessageEvent e)
    {
        if (string.IsNullOrEmpty(e.SessionKey)) return;

        lock (_lock)
        {
            var rec = GetOrCreate(e.SessionKey);
            SessionRecord.AddAndTrim(rec.HistoryMessages, e);
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
                // Keep one state event but clear operational fields
                if (rec.States.Count > 0)
                {
                    var st = rec.States[^1];
                    rec.States.Clear();
                    rec.States.Add(st with
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
                    });
                }
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
    public static string Aborted = $"[{ThemeProvider.Current.Tools.Messages.LogError}]•[/]";
    public static string ToolExecuting = $"[{ThemeProvider.Current.Tools.Messages.Working}]▶[/]";
    public static string Finished => $"[{ThemeProvider.Current.Tools.Messages.Success}]•[/]";
    public static string Spawning = $"[{ThemeProvider.Current.Tools.Messages.Working}]▶[/]";
    public static string UnknownSubagent = "◘";
    public static string Yielding = $"[{ThemeProvider.Current.Tools.Messages.Working}]▶[/]";
    public static string Unknown = "•";
}

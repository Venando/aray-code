namespace OpenClawPTT.Services;

/// <summary>
/// Per-agent store that holds gateway events organised by type.
/// Replaces <see cref="IAgentStatusTracker"/> — each event type owns
/// its fields authoritatively; no cross-event merging required.
///
/// Query methods pool from stored event collections — e.g. last activity
/// time comes from the most recent event that carries an EndedAt / Ts.
/// </summary>
public interface IAgentActivityStore
{
    // ── Identity / state ──────────────────────────────────────────────────

    /// <summary>The latest session state (replaced wholesale on sessions.changed).</summary>
    SessionStateEvent? GetSessionState(string sessionKey);

    /// <summary>All tracked session keys (for MainAgentsPart / bottom panel).</summary>
    IReadOnlyList<string> GetTrackedSessions();

    /// <summary>Session keys of subagents spawned by a given parent.</summary>
    IReadOnlyList<string> GetSubagents(string parentSessionKey);

    /// <summary>Model name for a session, from the latest state event.</summary>
    string? GetModel(string sessionKey);

    // ── Last-activity queries ─────────────────────────────────────────────

    /// <summary>Most recent assistant message for the session.</summary>
    AssistantMessageEvent? GetLastAssistantMessage(string sessionKey);

    /// <summary>Most recent tool call for the session.</summary>
    ToolEvent? GetLastToolCall(string sessionKey);

    /// <summary>Most recent user message for the session.</summary>
    UserMessageEvent? GetLastUserMessage(string sessionKey);

    /// <summary>Most recent lifecycle event for the session.</summary>
    AgentLifecycleEvent? GetLastLifecycle(string sessionKey);

    /// <summary>Most recent history message for the session.</summary>
    HistoryMessageEvent? GetLastHistoryMessage(string sessionKey);

    /// <summary>
    /// Timestamp (Unix ms) of the most recent activity for this session.
    /// Pools from EndedAt, Ts, Timestamp across all stored event types.
    /// </summary>
    long? GetLastActivityTime(string sessionKey);

    /// <summary>Short label for the type of the last activity ("tool", "message", etc.).</summary>
    string? GetActivityType(string sessionKey);

    /// <summary>
    /// Picks the most recent of (history message, tool call, assistant message) for the
    /// session and dispatches to the matching handler. Returns <see langword="default"/>
    /// when none of the three events exist.
    /// </summary>
    TResult? SelectLatestActivity<TResult>(
        string sessionKey,
        Func<HistoryMessageEvent, TResult> onHistory,
        Func<ToolEvent, TResult> onTool,
        Func<AssistantMessageEvent, TResult> onAssistant);

    // ── Derived display ───────────────────────────────────────────────────

    /// <summary>Status emoji derived from the current session state.</summary>
    string GetStatusEmoji(string sessionKey);

    // ── Mutations ─────────────────────────────────────────────────────────

    void Store(SessionStateEvent e);
    void Store(AssistantMessageEvent e);
    void Store(ToolEvent e);
    void Store(UserMessageEvent e);
    void Store(AgentLifecycleEvent e);
    void Store(AgentItemEvent e);
    void Store(HistoryMessageEvent e);

    void Remove(string sessionKey);
    void Reset(string sessionKey);

    // ── Change notification ───────────────────────────────────────────────

    /// <summary>Fires when any data changes. Parameter is the affected session key.</summary>
    event Action<string>? Changed;
}

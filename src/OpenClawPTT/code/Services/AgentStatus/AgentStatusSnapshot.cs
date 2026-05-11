using System.Text.Json;

namespace OpenClawPTT.Services;

/// <summary>
/// Immutable snapshot of an agent or subagent's status.
/// Stores all extractable data from gateway payloads for future use.
/// </summary>
public sealed record AgentStatusSnapshot
{
    // ── Identity ─────────────────────────────────────────────────────────────

    /// <summary>Logical session key, e.g. "agent:anime:main".</summary>
    public string SessionKey { get; init; } = string.Empty;

    /// <summary>UUID form of the session (sessionId field).</summary>
    public string? SessionId { get; init; }

    /// <summary>Session key of the parent agent (subagents only).</summary>
    public string? ParentSessionKey { get; init; }

    /// <summary>
    /// The raw "spawnedBy" field as it arrives in subagent payloads.
    /// Typically identical to ParentSessionKey but preserved separately.
    /// </summary>
    public string? SpawnedBy { get; init; }

    /// <summary>Human-readable display name.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Session kind, e.g. "direct".</summary>
    public string? Kind { get; init; }

    // ── Run / event envelope ─────────────────────────────────────────────────

    /// <summary>
    /// ID of the most recent run associated with this session
    /// (from the "runId" field on lifecycle/tool/assistant stream events).
    /// </summary>
    public string? RunId { get; init; }

    /// <summary>
    /// Gateway event phase at the time this snapshot was built,
    /// e.g. "start", "message", "end".
    /// </summary>
    public string? Phase { get; init; }

    /// <summary>
    /// Gateway stream type at the time this snapshot was built,
    /// e.g. "lifecycle", "tool", "item", "assistant".
    /// </summary>
    public string? Stream { get; init; }

    /// <summary>
    /// Reason for the most recent session event, e.g. "create", "send".
    /// </summary>
    public string? EventReason { get; init; }

    /// <summary>Monotonic sequence number within the current run.</summary>
    public int? Seq { get; init; }

    // ── Operational state ─────────────────────────────────────────────────────

    /// <summary>Agent status string, e.g. "running", "done".</summary>
    public string? Status { get; init; }

    /// <summary>Why the last LLM turn ended, e.g. "stop", "toolUse", "aborted".</summary>
    public string? StopReason { get; init; }

    /// <summary>Whether the last run was aborted.</summary>
    public bool? AbortedLastRun { get; init; }

    /// <summary>Subagent lifecycle state, e.g. "active", "historical".</summary>
    public string? SubagentRunState { get; init; }

    /// <summary>True when a subagent run is currently live.</summary>
    public bool? HasActiveSubagentRun { get; init; }

    // ── Model / provider ──────────────────────────────────────────────────────

    public string? Model { get; init; }
    public string? ModelProvider { get; init; }

    /// <summary>
    /// Agent runtime descriptor (from the "agentRuntime" object),
    /// stored as "{id}:{source}", e.g. "pi:implicit".
    /// </summary>
    public string? AgentRuntimeId { get; init; }

    // ── Tokens & cost ─────────────────────────────────────────────────────────

    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public long? TotalTokens { get; init; }

    /// <summary>True when token counts were freshly computed for this event.</summary>
    public bool? TotalTokensFresh { get; init; }

    public long? ContextTokens { get; init; }

    /// <summary>Estimated run cost in USD.</summary>
    public decimal? EstimatedCostUsd { get; init; }

    // ── Timing ────────────────────────────────────────────────────────────────

    public long? StartedAt { get; init; }
    public long? EndedAt { get; init; }
    public long? RuntimeMs { get; init; }
    public long? UpdatedAt { get; init; }

    // ── Subagent metadata ─────────────────────────────────────────────────────

    /// <summary>Role of this subagent, e.g. "orchestrator".</summary>
    public string? SubagentRole { get; init; }

    /// <summary>How many levels deep this subagent was spawned.</summary>
    public int? SpawnDepth { get; init; }

    /// <summary>Scope of subagent control, e.g. "children".</summary>
    public string? SubagentControlScope { get; init; }

    /// <summary>Workspace directory inherited from the spawning agent.</summary>
    public string? SpawnedWorkspaceDir { get; init; }

    /// <summary>Child session keys spawned by this agent in the current run.</summary>
    public IReadOnlyList<string> ChildSessions { get; init; } = Array.Empty<string>();

    // ── Channel / delivery ────────────────────────────────────────────────────

    /// <summary>Primary delivery channel, e.g. "webchat".</summary>
    public string? Channel { get; init; }

    /// <summary>Most recent channel used, e.g. "webchat".</summary>
    public string? LastChannel { get; init; }

    /// <summary>Chat type, e.g. "direct".</summary>
    public string? ChatType { get; init; }

    /// <summary>
    /// Origin provider/surface descriptor,
    /// stored as "{provider}:{surface}", e.g. "webchat:webchat".
    /// </summary>
    public string? OriginProvider { get; init; }

    /// <summary>Whether the system has sent at least one message this session.</summary>
    public bool? SystemSent { get; init; }

    // ── Thinking / model options ──────────────────────────────────────────────

    /// <summary>Default thinking level configured for this session, e.g. "high".</summary>
    public string? ThinkingDefault { get; init; }

    // ── Compaction ────────────────────────────────────────────────────────────

    public int? CompactionCheckpointCount { get; init; }

    /// <summary>
    /// ID of the latest compaction checkpoint, for tracking context compaction.
    /// </summary>
    public string? LatestCompactionCheckpointId { get; init; }

    public long? LatestCompactionCheckpointCreatedAt { get; init; }

    // ── Derived helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// True if this snapshot represents a subagent.
    /// Detects via explicit <see cref="ParentSessionKey"/> OR the sessionKey
    /// pattern <c>agent:*:subagent:*</c>.
    /// </summary>
    public bool IsSubagent => !string.IsNullOrEmpty(ParentSessionKey)
        || SessionKey.Contains(":subagent:", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the agent/subagent is actively generating or waiting for a
    /// tool result (i.e. the LLM turn has not yet ended for this run).
    /// </summary>
    public bool IsRunning => Status?.Equals("running", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// True when a subagent run is live (<see cref="HasActiveSubagentRun"/> == true
    /// and <see cref="SubagentRunState"/> == "active").
    /// Always false for main agents.
    /// </summary>
    public bool IsSubagentActive =>
        IsSubagent
        && HasActiveSubagentRun == true
        && SubagentRunState?.Equals("active", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// True when a subagent has been created/announced by the gateway but its
    /// own lifecycle run has not yet started (no <see cref="SubagentRunState"/>
    /// present yet, but the session record exists).
    /// </summary>
    public bool IsSubagentSpawning =>
        IsSubagent
        && string.IsNullOrEmpty(SubagentRunState)
        && IsRunning;

    /// <summary>
    /// True when the main agent has live children and is waiting via
    /// <c>sessions_yield</c> for them to complete.
    /// Detected by: running + has child sessions + the last run phase was
    /// "end" with liveness "abandoned" (the run yielded, not finished), or
    /// simply that <see cref="ChildSessions"/> is non-empty while the main
    /// agent's own last run ended without a clean <c>stop</c> stopReason.
    /// </summary>
    public bool IsYieldingForChildren =>
        !IsSubagent
        && ChildSessions.Count > 0
        && Phase == "end"
        && Status == "done"
        && StopReason is not ("stop" or "aborted");

    /// <summary>
    /// True when the agent/subagent has completed its work cleanly.
    /// </summary>
    public bool IsFinished =>
        // Subagent: run archived and nothing active.
        (IsSubagent
            && SubagentRunState?.Equals("historical", StringComparison.OrdinalIgnoreCase) == true
            && HasActiveSubagentRun != true)
        // Main agent: explicit stop with no pending children.
        || (!IsSubagent
            && StopReason == "stop"
            && ChildSessions.Count == 0);

    /// <summary>
    /// True when the run was explicitly aborted.
    /// Covers both <c>stopReason == "aborted"</c> and <c>abortedLastRun == true</c>.
    /// </summary>
    public bool IsAborted =>
        StopReason?.Equals("aborted", StringComparison.OrdinalIgnoreCase) == true
        || AbortedLastRun == true;

    /// <summary>
    /// True when the agent is in the middle of executing a tool call
    /// (LLM stopped at <c>toolUse</c> and the tool result has not arrived yet).
    /// </summary>
    public bool IsUsingTool =>
        StopReason?.Equals("toolUse", StringComparison.OrdinalIgnoreCase) == true;

    // Status emoji constants — single source of truth for all agent state visuals.
    // Use these instead of literal emoji strings everywhere.
    public const string AbortedEmoji = "⏳";
    public const string ToolExecutingEmoji = "🔄";
    public const string FinishedEmoji = "✅";
    public const string SpawningEmoji = "⏳";
    public const string UnknownSubagentEmoji = "⚪";
    public const string YieldingEmoji = "⏳";
    public const string ReadyEmoji = "🟢";

    /// <summary>Returns a single emoji representing the agent's current state.</summary>
    public string GetStatusEmoji()
    {
        // Aborted — highest priority, applies to both agent types
        if (IsAborted) return AbortedEmoji;

        // Tool mid-execution
        if (IsUsingTool) return ToolExecutingEmoji;

        // Subagent states
        if (IsSubagent)
        {
            // Completed run (archived).
            if (IsFinished) return FinishedEmoji;

            // Actively running its LLM turn or waiting for its own tool.
            if (IsSubagentActive) return ToolExecutingEmoji;

            // Created/announced but its own lifecycle run hasn't started yet.
            if (IsSubagentSpawning) return SpawningEmoji;

            // Transitional / unknown state (e.g. mid-handshake payloads).
            return UnknownSubagentEmoji;
        }

        // Main agent states

        // Yielded: run ended (phase == "end", status == "done") but children
        // are still live. The main agent is not truly done — it's waiting.
        if (IsYieldingForChildren) return YieldingEmoji;

        // Actively generating / ready to listen.
        if (IsRunning) return ReadyEmoji;

        // Unknown / initial state.
        return ReadyEmoji;
    }
}
using System.Text.Json;

namespace OpenClawPTT.Services;

// ═══════════════════════════════════════════════════════════════════════════════
// Gateway event type catalogue
//
// Design principle: every event type owns exactly the fields it emits authoritatively.
// No merging across types. The caller stores whichever records it cares about.
//
// Event routing map (from observed wire traffic):
//
//   event == "sessions.changed"   → SessionStateEvent      (always has full session object)
//   event == "session.message"    → AssistantMessageEvent | UserMessageEvent (by role)
//   event == "session.tool"       → ToolEvent
//   event == "agent"              → AgentLifecycleEvent | AgentStreamEvent | AgentItemEvent (by stream)
//   event == "chat"               → ChatStreamEvent
//   event == "presence"           → ignored (infrastructure)
//   event == "tick"               → ignored (infrastructure)
//   event == "heartbeat"          → ignored (infrastructure)
//   event == "health"             → ignored (infrastructure)
// ═══════════════════════════════════════════════════════════════════════════════


// ── 1. sessions.changed ───────────────────────────────────────────────────────
// This is the authoritative snapshot of session state. It always carries the
// full nested `session` object AND a flat copy of every field at the top level.
// Replace your stored snapshot wholesale whenever this arrives — no merging needed.

/// <summary>
/// Extracted from event == "sessions.changed".
/// The session object is always complete; replace (don't merge) the stored snapshot.
/// </summary>
public sealed record SessionStateEvent
{
    public required string SessionKey { get; init; }

    // ── Envelope (top-level payload, not inside session) ─────────────────────
    public string? Phase { get; init; }           // "start" | "message" | "end"
    public string? RunId { get; init; }
    public string? Reason { get; init; }          // "create" | "send" | null
    public long? Ts { get; init; }
    public string? MessageId { get; init; }
    public int? MessageSeq { get; init; }

    // ── Full session state (from the nested `session` object) ────────────────
    public string? SessionId { get; init; }
    public string? Kind { get; init; }
    public string? ChatType { get; init; }
    public string? Status { get; init; }          // "running" | "done"
    public bool? AbortedLastRun { get; init; }
    public bool? SystemSent { get; init; }

    public string? Model { get; init; }
    public string? ModelProvider { get; init; }
    public string? AgentRuntimeId { get; init; } // "{id}:{source}"

    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public long? TotalTokens { get; init; }
    public bool? TotalTokensFresh { get; init; }
    public long? ContextTokens { get; init; }
    public decimal? EstimatedCostUsd { get; init; }

    public long? StartedAt { get; init; }
    public long? EndedAt { get; init; }
    public long? RuntimeMs { get; init; }
    public long? UpdatedAt { get; init; }

    public string? Channel { get; init; }
    public string? LastChannel { get; init; }
    public string? OriginProvider { get; init; }  // "{provider}:{surface}"
    public string? DisplayName { get; init; }

    public string? ThinkingDefault { get; init; }

    public int? CompactionCheckpointCount { get; init; }
    public string? LatestCompactionCheckpointId { get; init; }
    public long? LatestCompactionCheckpointCreatedAt { get; init; }

    public IReadOnlyList<string> ChildSessions { get; init; } = Array.Empty<string>();

    // ── Subagent-only fields (only populated when reason == "create") ─────────
    public string? ParentSessionKey { get; init; }
    public string? SpawnedBy { get; init; }
    public int? SpawnDepth { get; init; }
    public string? SubagentRole { get; init; }
    public string? SubagentControlScope { get; init; }
    public string? SpawnedWorkspaceDir { get; init; }
    public string? SubagentRunState { get; init; }    // "active" | "historical"
    public bool? HasActiveSubagentRun { get; init; }
}


// ── 2. session.message (role == "assistant") ──────────────────────────────────
// Owns: model, provider, usage (tokens + cost), stopReason, responseId.
// Does NOT own: status, phase, childSessions — those come from sessions.changed.

/// <summary>
/// Extracted from event == "session.message" where message.role == "assistant".
/// The authoritative source for per-message token usage and stop reason.
/// </summary>
public sealed record AssistantMessageEvent
{
    public required string SessionKey { get; init; }
    public required string MessageId { get; init; }
    public required int MessageSeq { get; init; }
    public string? RunId { get; init; }

    public string? Model { get; init; }
    public string? ModelProvider { get; init; }   // message.provider
    public string? StopReason { get; init; }       // message.stopReason
    public string? ResponseId { get; init; }
    public long? Timestamp { get; init; }

    // From message.usage
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public long? TotalTokens { get; init; }
    public long? CacheRead { get; init; }
    public long? CacheWrite { get; init; }
    public decimal? CostTotal { get; init; }       // usage.cost.total

    // Content blocks from message.content[] array
    public string? ContentText { get; init; }       // first text block (convenience)
    public IReadOnlyList<ContentBlock> ContentBlocks { get; init; } = Array.Empty<ContentBlock>();
}

/// <summary>Individual block from a message content[] array.</summary>
public sealed record ContentBlock
{
    public required string Type { get; init; }
    public string? Text { get; init; }           // Type == "text"
    public string? Thinking { get; init; }         // Type == "thinking"
}


// ── 3. session.message (role == "user") ──────────────────────────────────────
// Lightweight — we just need to know a user message arrived and its seq.

/// <summary>
/// Extracted from event == "session.message" where message.role == "user".
/// </summary>
public sealed record UserMessageEvent
{
    public required string SessionKey { get; init; }
    public string? MessageId { get; init; }
    public int? MessageSeq { get; init; }
    public long? Timestamp { get; init; }
    public string? ContentText { get; init; }      // first text block if present
}


// ── 4. session.tool ───────────────────────────────────────────────────────────
// Owns: tool lifecycle (start / result), tool name, args, result payload.

/// <summary>
/// Extracted from event == "session.tool".
/// Carries both the start and result phases of a tool call.
/// </summary>
public sealed record ToolEvent
{
    public required string SessionKey { get; init; }
    public required string RunId { get; init; }
    public required string ToolCallId { get; init; }
    public required string ToolName { get; init; }
    public required string Phase { get; init; }    // "start" | "result"
    public int? Seq { get; init; }
    public long? Ts { get; init; }

    // Phase == "start"
    public string? ArgsJson { get; init; }

    // Phase == "result"
    public bool? IsError { get; init; }
    public string? ResultText { get; init; }       // first text content block
    public string? ResultDetailsJson { get; init; }
}


// ── 5. agent (stream == "lifecycle") ─────────────────────────────────────────
// Owns: run start/end timing. Lighter than sessions.changed — use that for status.

/// <summary>
/// Extracted from event == "agent", stream == "lifecycle".
/// Signals run boundary; carries precise start/end timestamps from the engine.
/// </summary>
public sealed record AgentLifecycleEvent
{
    public required string SessionKey { get; init; }
    public required string RunId { get; init; }
    public required string Phase { get; init; }    // "start" | "end"
    public int? Seq { get; init; }
    public long? Ts { get; init; }

    // Phase == "start"
    public long? StartedAt { get; init; }

    // Phase == "end"
    public long? EndedAt { get; init; }
    public string? LivenessState { get; init; }    // "working" | "abandoned" etc.
}


// ── 6. agent (stream == "assistant") ─────────────────────────────────────────
// Streaming text deltas — for display only, not stored in snapshot.

/// <summary>
/// Extracted from event == "agent", stream == "assistant".
/// Streaming text chunk — display only, not persisted to snapshot.
/// </summary>
public sealed record AgentStreamEvent
{
    public required string SessionKey { get; init; }
    public required string RunId { get; init; }
    public int? Seq { get; init; }
    public long? Ts { get; init; }
    public string? Delta { get; init; }
    public string? Text { get; init; }             // accumulated so far
}


// ── 7. agent (stream == "item") ──────────────────────────────────────────────
// Tool item lifecycle within a run — for UI progress display.

/// <summary>
/// Extracted from event == "agent", stream == "item".
/// Tracks individual tool-call items (start/end) within a run for progress display.
/// </summary>
public sealed record AgentItemEvent
{
    public required string SessionKey { get; init; }
    public required string RunId { get; init; }
    public required string ItemId { get; init; }
    public required string Phase { get; init; }    // "start" | "end"
    public string? Kind { get; init; }             // "tool"
    public string? Name { get; init; }
    public string? Title { get; init; }
    public string? Status { get; init; }           // "running" | "completed"
    public string? ToolCallId { get; init; }
    public int? Seq { get; init; }
    public long? Ts { get; init; }
    public long? StartedAt { get; init; }
    public long? EndedAt { get; init; }
}


// ── 8. chat ───────────────────────────────────────────────────────────────────
// Streaming message state — for display/TTS trigger only.

/// <summary>
/// Extracted from event == "chat".
/// "delta" = chunk arrived; "final" = run turn complete.
/// </summary>
public sealed record ChatStreamEvent
{
    public required string SessionKey { get; init; }
    public required string RunId { get; init; }
    public required string State { get; init; }    // "delta" | "final"
    public int? Seq { get; init; }
    public string? MessageText { get; init; }      // null on "final" when no content
}


// ── 9. Chat history messages (chat.history / sessions.preview) ───────────────
// These use a different schema from live gateway events.
// See UserMessageHelper.cs for the full JSON shape.

/// <summary>Single tool call inside a history message content block.</summary>
public sealed record HistoryToolCall
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? ArgumentsJson { get; init; }
}

/// <summary>
/// Extracted from chat.history / sessions.preview response entries.
/// Covers role=="assistant" and role=="user".  Tool calls are extracted
/// from the content[] array into <see cref="ToolCalls"/>.
/// </summary>
public sealed record HistoryMessageEvent
{
    public required string SessionKey { get; init; }
    public required string Role { get; init; }      // "assistant" | "user"

    // ── Message envelope ────────────────────────────────────────────────
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? StopReason { get; init; }
    public long? Timestamp { get; init; }
    public string? ResponseId { get; init; }
    public string? Api { get; init; }

    // ── Usage ───────────────────────────────────────────────────────────
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public long? TotalTokens { get; init; }
    public long? CacheRead { get; init; }
    public long? CacheWrite { get; init; }
    public decimal? CostTotal { get; init; }

    // ── Content ─────────────────────────────────────────────────────────
    public string? ContentText { get; init; }
    public IReadOnlyList<HistoryToolCall> ToolCalls { get; init; } = Array.Empty<HistoryToolCall>();
    public IReadOnlyList<string> ThinkingBlocks { get; init; } = Array.Empty<string>();

    // ── Sender (untrusted metadata) ─────────────────────────────────────
    public string? SenderLabel { get; init; }
    public string? SenderId { get; init; }

    // ── __openclaw metadata ─────────────────────────────────────────────
    public string? OpenClawId { get; init; }
    public int? OpenClawSeq { get; init; }
}
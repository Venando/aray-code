using System.Text.Json;

namespace OpenClawPTT.Services;

/// <summary>
/// Immutable snapshot of an agent or subagent's status.
/// Stores all extractable data from gateway payloads for future use.
/// </summary>
public sealed class AgentStatusSnapshot
{
    public string SessionKey { get; init; } = string.Empty;
    public string? ParentSessionKey { get; init; }
    public string? DisplayName { get; init; }
    public string? Status { get; init; }
    public string? StopReason { get; init; }
    public string? Model { get; init; }
    public string? ModelProvider { get; init; }
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public long? TotalTokens { get; init; }
    public long? StartedAt { get; init; }
    public long? EndedAt { get; init; }
    public long? RuntimeMs { get; init; }
    public string? SubagentRunState { get; init; }
    public bool? HasActiveSubagentRun { get; init; }
    public IReadOnlyList<string> ChildSessions { get; init; } = Array.Empty<string>();
    public string? SubagentRole { get; init; }
    public int? SpawnDepth { get; init; }
    public long? ContextTokens { get; init; }
    public string? LastChannel { get; init; }
    public bool? AbortedLastRun { get; init; }
    public long? UpdatedAt { get; init; }

    /// <summary>
    /// True if this snapshot represents a subagent (has a parent session key).
    /// </summary>
    public bool IsSubagent => !string.IsNullOrEmpty(ParentSessionKey);

    /// <summary>
    /// True if the agent appears to be actively running.
    /// </summary>
    public bool IsRunning => Status?.Equals("running", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// True if the agent has finished its current task (stop or aborted).
    /// </summary>
    public bool IsFinished => StopReason is "stop" or "aborted";

    /// <summary>
    /// Returns a short display label for the bottom panel.
    /// </summary>
    public string GetStatusLabel()
    {
        if (IsSubagent)
        {
            if (SubagentRunState == "historical" || IsFinished)
                return "[grey]done[/]";
            if (HasActiveSubagentRun == true)
                return "[green]running[/]";
            return "[yellow]waiting[/]";
        }

        // Main agent
        if (Status == "done")
            return "[grey]idle[/]";
        if (IsFinished)
            return "[yellow]waiting[/]";
        return "[green]running[/]";
    }
}

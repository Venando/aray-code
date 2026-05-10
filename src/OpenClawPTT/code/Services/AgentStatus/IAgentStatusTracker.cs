namespace OpenClawPTT.Services;

/// <summary>
/// Thread-safe tracker that stores agent and subagent status snapshots.
/// Populated from gateway payloads before the active-session filter drops them.
/// </summary>
public interface IAgentStatusTracker
{
    /// <summary>Updates or inserts a snapshot for the given session key.</summary>
    void Update(AgentStatusSnapshot snapshot);

    /// <summary>Removes a session from tracking (e.g. subagent finished).</summary>
    void Remove(string sessionKey);

    /// <summary>Gets a snapshot by session key, or null.</summary>
    AgentStatusSnapshot? Get(string sessionKey);

    /// <summary>All tracked sessions.</summary>
    IReadOnlyList<AgentStatusSnapshot> All { get; }

    /// <summary>Event raised when any snapshot changes.</summary>
    event Action? Changed;

    /// <summary>
    /// Returns the active main agent (non-subagent) snapshot, or null.
    /// </summary>
    AgentStatusSnapshot? GetMainAgent();

    /// <summary>
    /// Returns all subagents belonging to a given parent session key.
    /// </summary>
    IReadOnlyList<AgentStatusSnapshot> GetSubagents(string parentSessionKey);
}

using System.Collections.Generic;

namespace ArayCode;

/// <summary>Top-level container for agents.json.</summary>
public sealed class AgentsConfig
{
    public List<AgentPersistedSettings> Agents { get; set; } = new();
}

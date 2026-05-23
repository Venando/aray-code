using System.Collections.Generic;

namespace ArayCode;

/// <summary>Information about an agent available in the current OpenClaw gateway connection.</summary>
public sealed class AgentInfo
{
    public string AgentId { get; init; } = "";
    public string Name { get; init; } = "";
    public string SessionKey { get; init; } = "";
    public bool IsDefault { get; init; }
}

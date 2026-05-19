using System.Text.Json;

namespace OpenClawPTT.Services;

/// <summary>
/// Renders a tool's arguments into a short one-line description
/// for the agent-status bottom panel.
/// </summary>
public interface IAgentActivityRenderer
{
    string ToolName { get; }
    string Render(JsonElement args);
}

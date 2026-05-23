using System.Text.Json;

namespace ArayCode.Services;

internal sealed class SubagentsActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "subagents";

    public string Render(JsonElement args)
    {
        if (!args.TryGetProperty("action", out var actionProp))
            return "Managing subagents";

        string action = actionProp.GetString() ?? "unknown";

        return action switch
        {
            "list" => "Listing subagents",
            "kill" => "Killing subagent",
            "steer" => "Steering subagent",
            _ => $"Subagents: {action}"
        };
    }
}

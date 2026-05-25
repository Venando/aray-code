using System.Text.Json;

namespace ArayCode.Services;

internal sealed class SessionsSpawnActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "sessions_spawn";

    public string Render(JsonElement args)
    {
        var label = AgentActivityRendererHelpers.GetString(args, "label");
        if (label is not null)
            return $"Spawning: {label}";

        var task = AgentActivityRendererHelpers.GetString(args, "task");
        if (task is not null)
            return $"Spawning: {task.Replace('\n', ' ')}";

        return "Spawning subagent";
    }
}

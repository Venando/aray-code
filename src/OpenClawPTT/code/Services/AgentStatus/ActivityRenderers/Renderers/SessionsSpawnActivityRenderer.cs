using System.Text.Json;

namespace OpenClawPTT.Services;

internal sealed class SessionsSpawnActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "sessions_spawn";

    public string Render(JsonElement args)
    {
        var label = AgentActivityRendererHelpers.GetString(args, "label");
        if (label is not null)
            return $"Spawning: {AgentActivityRendererHelpers.Truncate(label, 40)}";

        var task = AgentActivityRendererHelpers.GetString(args, "task");
        if (task is not null)
            return $"Spawning: {AgentActivityRendererHelpers.Truncate(task.Replace('\n', ' '), 40)}";

        return "Spawning subagent";
    }
}

using System.Text.Json;

namespace ArayCode.Services;

internal sealed class ProcessActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "process";

    public string Render(JsonElement args)
    {
        if (args.TryGetProperty("action", out var actionProp))
        {
            string action = actionProp.GetString() ?? "unknown";
            
            if (args.TryGetProperty("sessionId", out var sessionIdProp))
            {
                return $"Process {action}: {sessionIdProp.GetString() ?? ""}";
            }

            return $"Process {action}";
        }

        return "Managing process";
    }
}

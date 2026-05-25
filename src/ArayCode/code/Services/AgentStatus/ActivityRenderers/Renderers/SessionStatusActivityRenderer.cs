using System.Text.Json;

namespace ArayCode.Services;

internal sealed class SessionStatusActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "session_status";

    public string Render(JsonElement args)
    {
        var key = AgentActivityRendererHelpers.GetString(args, "sessionKey");
        return key is not null
            ? $"Checking status of {key}"
            : "Checking session status";
    }
}

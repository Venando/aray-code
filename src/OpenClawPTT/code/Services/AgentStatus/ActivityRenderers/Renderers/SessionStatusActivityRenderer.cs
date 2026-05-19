using System.Text.Json;

namespace OpenClawPTT.Services;

internal sealed class SessionStatusActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "session_status";

    public string Render(JsonElement args)
    {
        var key = AgentActivityRendererHelpers.GetString(args, "sessionKey");
        return key is not null
            ? $"Checking status of {AgentActivityRendererHelpers.Truncate(key, 30)}"
            : "Checking session status";
    }
}

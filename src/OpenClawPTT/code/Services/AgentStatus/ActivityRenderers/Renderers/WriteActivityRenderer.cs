using System.Text.Json;

namespace OpenClawPTT.Services;

internal sealed class WriteActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "write";

    public string Render(JsonElement args)
    {
        var path = AgentActivityRendererHelpers.GetString(args, "path");
        return path is not null
            ? $"Writing {AgentActivityRendererHelpers.ShortenPath(path)}"
            : "Writing file";
    }
}

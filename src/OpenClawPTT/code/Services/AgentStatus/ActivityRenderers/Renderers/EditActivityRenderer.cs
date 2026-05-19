using System.Text.Json;

namespace OpenClawPTT.Services;

internal sealed class EditActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "edit";

    public string Render(JsonElement args)
    {
        var path = AgentActivityRendererHelpers.GetString(args, "path");
        return path is not null
            ? $"Editing {AgentActivityRendererHelpers.ShortenPath(path)}"
            : "Editing file";
    }
}

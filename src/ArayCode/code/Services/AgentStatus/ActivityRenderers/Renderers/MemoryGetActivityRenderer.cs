using System.Text.Json;

namespace ArayCode.Services;

internal sealed class MemoryGetActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "memory_get";

    public string Render(JsonElement args)
    {
        var path = AgentActivityRendererHelpers.GetString(args, "path");
        if (path is null) return "Reading memory";

        var display = AgentActivityRendererHelpers.ShortenPath(path);

        if (args.TryGetProperty("from", out var fromProp) &&
            args.TryGetProperty("lines", out var linesProp))
        {
            return $"Reading memory {display} (lines {fromProp.GetInt32()}-{fromProp.GetInt32() + linesProp.GetInt32()})";
        }

        return $"Reading memory {display}";
    }
}

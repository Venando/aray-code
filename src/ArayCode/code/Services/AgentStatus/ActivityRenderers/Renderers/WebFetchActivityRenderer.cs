using System.Text.Json;

namespace ArayCode.Services;

internal sealed class WebFetchActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "web_fetch";

    public string Render(JsonElement args)
    {
        var url = AgentActivityRendererHelpers.GetString(args, "url");
        if (url is null) return "Fetching URL";

        var display = url.Replace("https://", "").Replace("http://", "");
        display = AgentActivityRendererHelpers.Truncate(display, 50);

        if (args.TryGetProperty("maxChars", out var maxCharsProp))
        {
            return $"Fetching {display} (max {maxCharsProp.GetInt32()} chars)";
        }

        return $"Fetching {display}";
    }
}

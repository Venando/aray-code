using System.Text.Json;
using ArayCode.Formatting;

namespace ArayCode.Services;

internal sealed class WebFetchActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "web_fetch";

    public string Render(JsonElement args)
    {
        var url = AgentActivityRendererHelpers.GetString(args, "url");
        if (url is null) return "Fetching URL";

        var display = url.Replace("https://", "").Replace("http://", "");

        var freeSpace = ConsoleMetrics.GetWindowWidth() - AgentStatusLineRenderer.AllMargins;

        string? maxCharsInfo = null;
        if (args.TryGetProperty("maxChars", out var maxCharsProp))
        {
            maxCharsInfo = $" (max {maxCharsProp.GetInt32()} chars)";
        }

        var prefix = "Fetching ";
        var prefixWidth = CharacterWidth.GetDisplayWidth(prefix);
        var suffixWidth = maxCharsInfo is not null ? CharacterWidth.GetDisplayWidth(maxCharsInfo) : 0;
        var availableForUrl = Math.Max(0, freeSpace - prefixWidth - suffixWidth);

        var urlWidth = CharacterWidth.GetDisplayWidth(display);
        if (urlWidth > availableForUrl && availableForUrl > 3)
        {
            display = AgentStatusLineRenderer.TruncateByDisplayWidth(display, availableForUrl - 1) + "…";
        }

        return prefix + display + (maxCharsInfo ?? "");
    }
}

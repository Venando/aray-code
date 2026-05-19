using System.Text.Json;

namespace OpenClawPTT.Services;

internal sealed class WebSearchActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "web_search";

    public string Render(JsonElement args)
    {
        var query = AgentActivityRendererHelpers.GetString(args, "query");
        if (query is null) return "Searching web";

        var display = AgentActivityRendererHelpers.Truncate(query.Replace('\n', ' '), 50);
        return $"Searching: {display}";
    }
}

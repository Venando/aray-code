using System.Text.Json;

namespace ArayCode.Services;

internal sealed class WebSearchActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "web_search";

    public string Render(JsonElement args)
    {
        var query = AgentActivityRendererHelpers.GetString(args, "query");
        if (query is null) return "Searching web";

        return $"Searching: {query.Replace('\n', ' ')}";
    }
}

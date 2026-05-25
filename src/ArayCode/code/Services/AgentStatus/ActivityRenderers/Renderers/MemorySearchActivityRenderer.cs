using System.Text.Json;

namespace ArayCode.Services;

internal sealed class MemorySearchActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "memory_search";

    public string Render(JsonElement args)
    {
        var query = AgentActivityRendererHelpers.GetString(args, "query");
        if (query is null) return "Searching memory";

        return $"Searching memory: {query.Replace('\n', ' ')}";
    }
}

using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class MemorySearchToolRenderer : ToolRendererBase
{
    public MemorySearchToolRenderer(IToolOutput output) : base(output)
    {
    }

    public override string ToolName => "memory_search";

    public override void Render(JsonElement args, int rightMarginIndent)
    {
        if (args.TryGetProperty("query", out var queryProp))
        {
            PrintValue(queryProp.GetString() ?? "", Style.General.Label);
        }
        PrintIntPropertyIfExists(args, "maxResults", "max results: ", prependComma: true);
        if (args.TryGetProperty("minScore", out var minScoreProp))
        {
            PrintLabelValue("min score: ", $"{minScoreProp.GetDouble():F2}", prependComma: true);
        }
    }
}

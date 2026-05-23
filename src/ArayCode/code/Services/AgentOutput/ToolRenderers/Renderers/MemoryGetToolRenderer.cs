using System.Text.Json;

namespace ArayCode.Services;

public sealed class MemoryGetToolRenderer : ToolRendererBase
{
    public MemoryGetToolRenderer(IToolOutput output) : base(output)
    {
    }

    public override string ToolName => "memory_get";

    public override void Render(JsonElement args, int rightMarginIndent)
    {
        if (args.TryGetProperty("path", out var pathProp))
        {
            PrintValue(pathProp.GetString() ?? "", Style.General.Label);
        }
        PrintIntPropertyIfExists(args, "from", "from: ", prependComma: true);
        PrintIntPropertyIfExists(args, "lines", "lines: ", prependComma: true);
    }
}

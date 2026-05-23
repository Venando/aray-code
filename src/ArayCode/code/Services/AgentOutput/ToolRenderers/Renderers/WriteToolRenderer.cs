using System.Text.Json;

namespace ArayCode.Services;

public sealed class WriteToolRenderer : ToolRendererBase
{
    public WriteToolRenderer(IToolOutput output) : base(output)
    {
    }

    public override string ToolName => "write";

    public override void Render(JsonElement args, int rightMarginIndent)
    {
        if (args.TryGetProperty("path", out var pathProp))
        {
            Output.PrintLine(FilePathDisplayHelper.FormatDisplayPath(pathProp.GetString() ?? ""), Style.General.Label);
        }
    }
}

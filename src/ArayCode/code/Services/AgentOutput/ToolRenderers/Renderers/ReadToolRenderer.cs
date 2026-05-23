using System.Text.Json;

namespace ArayCode.Services;

public sealed class ReadToolRenderer : ToolRendererBase
{
    public ReadToolRenderer(IToolOutput output) : base(output)
    {
    }

    public override string ToolName => "read";

    public override void Render(JsonElement args, int rightMarginIndent)
    {
        if (args.TryGetProperty("file", out var fileProp) || args.TryGetProperty("path", out fileProp))
        {
            string displayPath = FilePathDisplayHelper.FormatDisplayPath(fileProp.GetString() ?? "");
            PrintValue(displayPath, Style.General.Label);
        }
        if (args.TryGetProperty("offset", out var offsetProp) &&
            args.TryGetProperty("limit", out var limitProp))
        {
            int offset = offsetProp.GetInt32();
            int limit = limitProp.GetInt32();
            Output.Print($" (lines {offset}-{offset + limit - 1})", Style.Reader.LineInfo);
        }
        else if (args.TryGetProperty("limit", out var limitProp2))
        {
            Output.Print($" (lines 1-{limitProp2.GetInt32()})", Style.Reader.LineInfo);
        }
    }
}

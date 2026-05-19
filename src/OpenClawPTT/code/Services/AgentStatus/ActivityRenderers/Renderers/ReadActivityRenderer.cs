using System.Text;
using System.Text.Json;

namespace OpenClawPTT.Services;

internal sealed class ReadActivityRenderer : IAgentActivityRenderer
{
    private readonly StringBuilder _sb = new();

    public string ToolName => "read";

    public string Render(JsonElement args)
    {
        _sb.Clear();
        _sb.Append("Reading ");
        
        if (args.TryGetProperty("file", out var fileProp) || args.TryGetProperty("path", out fileProp))
        {
            string displayPath = FilePathDisplayHelper.FormatDisplayPath(fileProp.GetString() ?? "");
            _sb.Append(displayPath);
        }

        if (args.TryGetProperty("offset", out var offsetProp) &&
            args.TryGetProperty("limit", out var limitProp))
        {
            int offset = offsetProp.GetInt32();
            int limit = limitProp.GetInt32();
            _sb.Append($" (lines {offset}-{offset + limit - 1})");
        }
        else if (args.TryGetProperty("limit", out var limitProp2))
        {
            _sb.Append($" (lines 1-{limitProp2.GetInt32()})");
        }

        return _sb.ToString();
    }
}

using System.Text.Json;

namespace ArayCode.Services;

public sealed class WebFetchToolRenderer : ToolRendererBase
{
    public WebFetchToolRenderer(IToolOutput output) : base(output)
    {
    }

    public override string ToolName => "web_fetch";

    public override void Render(JsonElement args, int rightMarginIndent)
    {
        if (args.TryGetProperty("url", out var urlProp))
        {
            string url = urlProp.GetString() ?? "";
            url = url.Replace("https://", "").Replace("http://", "");
            PrintValue(url, Style.Reader.FetchUrl);
        }
        if (args.TryGetProperty("maxChars", out var maxCharsProp))
        {
            Output.Print($" (max {maxCharsProp.GetInt32()} chars)", Style.Reader.FetchMaxInfo);
        }
    }
}

using System.Text.Json;

namespace ArayCode.Services;

public sealed class SessionsListToolRenderer : ToolRendererBase
{
    public SessionsListToolRenderer(IToolOutput output) : base(output)
    {
    }

    public override string ToolName => "sessions_list";

    public override void Render(JsonElement args, int rightMarginIndent)
    {
        bool hasPrinted = PrintIntPropertyIfExists(args, "limit", "limit: ");
        hasPrinted = PrintPropertyIfExists(args, "kinds", "kinds: ", prependComma: hasPrinted) || hasPrinted;
        hasPrinted = PrintIntPropertyIfExists(args, "messageLimit", "messages: ", prependComma: hasPrinted) || hasPrinted;
        
        if (args.TryGetProperty("activeMinutes", out var activeMinProp))
        {
            PrintLabelValue("in last ", $"{activeMinProp.GetInt32()} minutes", prependComma: hasPrinted);
        }
    }
}

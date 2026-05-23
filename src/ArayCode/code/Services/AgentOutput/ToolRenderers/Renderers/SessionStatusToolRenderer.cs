using System.Text.Json;

namespace ArayCode.Services;

public sealed class SessionStatusToolRenderer : ToolRendererBase
{
    public SessionStatusToolRenderer(IToolOutput output) : base(output)
    {
    }

    public override string ToolName => "session_status";

    public override void Render(JsonElement args, int rightMarginIndent)
    {
        PrintPropertyIfExists(args, "sessionKey", "key: ");
        PrintPropertyIfExists(args, "model", "model: ", prependComma: true);
    }
}

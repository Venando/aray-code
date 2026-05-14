using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class SessionsSpawnToolRenderer : ToolRendererBase
{
    public SessionsSpawnToolRenderer(IToolOutput output) : base(output)
    {
    }

    public override string ToolName => "sessions_spawn";

    public override void Render(JsonElement args, int rightMarginIndent)
    {
        if (args.TryGetProperty("label", out var labelProp))
        {
            PrintValue(labelProp.GetString() ?? "", Style.General.Label);
        }

        Output.PrintLine("", Style.General.Muted);

        if (args.TryGetProperty("task", out var taskProp))
        {
            string taskPrefix = "  ";
            Output.Print(taskPrefix, Style.General.Muted);
            Output.PrintTruncated(taskProp.GetString() ?? "", taskPrefix, rightMarginIndent, Style.General.Label, maxRows: 15);
        }
    }
}

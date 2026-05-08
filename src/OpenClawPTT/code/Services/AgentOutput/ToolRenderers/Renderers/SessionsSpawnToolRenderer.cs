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
            PrintValue(labelProp.GetString() ?? "", ConsoleColor.Gray);
        }
        bool hasPrinted = PrintPropertyIfExists(args, "runtime", "runtime: ", prependComma: true);
        hasPrinted = PrintPropertyIfExists(args, "mode", "mode: ", prependComma: hasPrinted) || hasPrinted;
        
        if (args.TryGetProperty("runTimeoutSeconds", out var timeoutProp))
        {
            PrintLabelValue("timeout: ", $"{timeoutProp.GetInt32()} seconds", prependComma: hasPrinted);
        }
        if (args.TryGetProperty("task", out var taskProp))
        {
            Output.PrintLine("", ConsoleColor.DarkGray);
            const string taskPrefix = "  Task: ";
            Output.Print(taskPrefix, ConsoleColor.DarkGray);
            Output.PrintTruncated(taskProp.GetString() ?? "", taskPrefix, rightMarginIndent, ConsoleColor.Gray, maxRows: 15);
        }
    }
}

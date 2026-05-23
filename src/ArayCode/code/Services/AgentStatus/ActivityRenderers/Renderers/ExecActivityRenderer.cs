using System.Text.Json;

namespace ArayCode.Services;

internal sealed class ExecActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "exec";

    public string Render(JsonElement args)
    {
        var cmd = AgentActivityRendererHelpers.GetString(args, "command");
        if (cmd is null) return "Running command";

        // For multi-line scripts, show compact preview
        var lines = cmd.Split('\n');
        if (lines.Length > 3)
        {
            var firstLine = lines[0].Trim();
            return "Running " + AgentActivityRendererHelpers.Truncate(firstLine, 50) +
                   $" (+{lines.Length - 1} lines)";
        }

        // Try to parse with TerminalCommandParser for better display
        var parsed = TerminalCommandParser.Parse(cmd);
        if (parsed.Count > 0)
        {
            var meta = parsed[0];
            var execName = System.IO.Path.GetFileName(meta.Executable);
            
            // Build argument summary
            var argSummary = BuildArgSummary(meta);
            if (!string.IsNullOrEmpty(argSummary))
                return $"Running {execName} {argSummary}";
            
            return $"Running {execName}";
        }

        var firstLineDirect = cmd.Split('\n')[0].Trim();
        return "Running " + AgentActivityRendererHelpers.Truncate(firstLineDirect, 60);
    }

    private static string BuildArgSummary(CommandMetadata meta)
    {
        var parts = new List<string>();

        // Add first few positionals
        int posCount = 0;
        foreach (var pos in meta.Positionals)
        {
            if (posCount >= 3) break;
            var formatted = FormatPositional(pos);
            if (!string.IsNullOrEmpty(formatted))
            {
                parts.Add(formatted);
                posCount++;
            }
        }

        if (meta.Positionals.Count > 3)
            parts.Add($"(+{meta.Positionals.Count - 3} more)");

        return string.Join(" ", parts);
    }

    private static string FormatPositional(string token)
    {
        if (token.Length > 30)
            return AgentActivityRendererHelpers.Truncate(token, 30);
        return token;
    }
}

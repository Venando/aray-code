using System.Text.Json;
using Spectre.Console;

namespace OpenClawPTT.Services;

public sealed class ExecToolRenderer : ToolRendererBase
{
    public ExecToolRenderer(IToolOutput output) : base(output)
    {
    }

    public override string ToolName => "exec";

    public override void Render(JsonElement args, int rightMarginIndent)
    {
        if (!args.TryGetProperty("command", out var cmdProp))
            return;

        string command = cmdProp.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(command))
            return;

        // If the command has many lines (e.g., a heredoc with a long script body),
        // show a compact truncated preview instead of the full parsed rendering.
        int lineCount = command.Split('\n').Length;
        const int maxPreviewLines = 6;
        if (lineCount > maxPreviewLines)
        {
            Output.PrintTruncated(command, "  ", rightMarginIndent, ConsoleColor.White, maxRows: maxPreviewLines);
            return;
        }

        var parsed = TerminalCommandParser.Parse(command);

        if (parsed.Count == 0)
        {
            PrintValue(command, ConsoleColor.Gray);
            return;
        }

        bool needNewline = false;
        foreach (var meta in parsed)
        {
            if (needNewline)
            {
                Output.PrintLine("", ConsoleColor.Gray);
            }

            RenderCommand(meta);
            needNewline = meta.IsChained;
        }
    }

    private void RenderCommand(CommandMetadata meta)
    {
        // ── Working directory prefix ────────────────────────────────────────
        if (!string.IsNullOrEmpty(meta.WorkingDirectory))
        {
            Output.Print("📂 ", ConsoleColor.DarkGray);
            Output.Print(FilePathDisplayHelper.FormatDisplayPath(meta.WorkingDirectory), ConsoleColor.DarkGray);
            Output.Print(" ", ConsoleColor.DarkGray);
        }

        // ── Inline env vars ────────────────────────────────────────────────
        if (meta.InlineEnv.Count > 0)
        {
            foreach (var kvp in meta.InlineEnv)
            {
                Output.Print($"{kvp.Key}=", ConsoleColor.Cyan);
                Output.Print($"{kvp.Value} ", ConsoleColor.Yellow);
            }
        }

        // ── Executable ─────────────────────────────────────────────────────
        string execName = System.IO.Path.GetFileName(meta.Executable);
        ConsoleColor execColor = GetExecutableColor(meta.Type);
        Output.Print(execName, execColor);

        // ── Positional arguments (with safety limit) ─────────────────────
        int posCount = 0;
        int posChars = 0;
        const int maxPositionals = 20;
        const int maxPosChars = 300;
        foreach (var pos in meta.Positionals)
        {
            if (posCount >= maxPositionals || posChars >= maxPosChars)
            {
                // Skip here-doc body tokens that leaked past parsing
                // and show a summary instead
                var remaining = meta.Positionals.Count - posCount;
                if (remaining > 0)
                    Output.Print($" ... ({remaining} more tokens)", ConsoleColor.DarkGray);
                break;
            }
            string fmt = FormatToken(pos);
            posChars += fmt.Length;
            Output.Print(" ", ConsoleColor.White);
            Output.Print(fmt, ConsoleColor.Cyan);
            posCount++;
        }

        // ── Script body (compact display) ────────────────────────────────────
        if (!string.IsNullOrEmpty(meta.ScriptBody))
        {
            Output.Print(" ", ConsoleColor.White);
            int bodyLen = meta.ScriptBody.Length;
            if (bodyLen <= 40)
            {
                Output.Print(meta.ScriptBody, ConsoleColor.DarkGray);
            }
            else
            {
                var preview = meta.ScriptBody.Replace('\n', ' ').Replace('\r', ' ');
                if (preview.Length > 37) preview = preview[..37] + "...";
                Output.Print(preview, ConsoleColor.DarkGray);
            }
        }

        // ── Flags ──────────────────────────────────────────────────────────
        foreach (var flag in meta.Flags)
        {
            if (flag.StartsWith("--"))
            {
                Output.Print(" ", ConsoleColor.White);
                Output.Print(FormatToken(flag), ConsoleColor.Green);
            }
            else
            {
                Output.Print(" ", ConsoleColor.White);
                Output.Print(FormatToken(flag), ConsoleColor.DarkYellow);
            }
        }

        // ── Redirects ─────────────────────────────────────────────────────
        foreach (var redir in meta.Redirects)
        {
            Output.Print(" ", ConsoleColor.White);
            Output.Print(FormatToken(redir), ConsoleColor.Gray);
        }

        // ── Pipe / chain indicators ────────────────────────────────────────
        if (meta.IsPiped)
        {
            Output.Print(" | ", ConsoleColor.Gray);
        }
        if (meta.IsChained)
        {
            Output.Print(" && ", ConsoleColor.Gray);
        }

        // ── Here-doc summary ───────────────────────────────────────────────
        if (meta.HereDoc != null)
        {
            Output.Print(" << '", ConsoleColor.Gray);
            Output.Print(meta.HereDoc.Delimiter, ConsoleColor.Gray);
            Output.Print("'", ConsoleColor.Gray);

            if (!string.IsNullOrEmpty(meta.HereDoc.TargetFile))
            {
                Output.Print(" > ", ConsoleColor.Gray);
                Output.Print(meta.HereDoc.TargetFile, ConsoleColor.Gray);
            }

            int bodyLines = meta.HereDoc.Body.Split('\n').Length;
            Output.Print($" ({bodyLines} line{(bodyLines == 1 ? "" : "s")})", ConsoleColor.DarkGray);
        }
    }

    private static ConsoleColor GetExecutableColor(CommandType type)
    {
        return type switch
        {
            CommandType.FileSystem => ConsoleColor.Green,
            CommandType.FileContent => ConsoleColor.Blue,
            CommandType.Build => ConsoleColor.Magenta,
            CommandType.PackageManager => ConsoleColor.Red,
            CommandType.Network => ConsoleColor.Cyan,
            CommandType.Scripting => ConsoleColor.Yellow,
            CommandType.Process => ConsoleColor.DarkYellow,
            CommandType.HereDoc => ConsoleColor.DarkCyan,
            CommandType.Vcs => ConsoleColor.DarkYellow,
            CommandType.Pipe => ConsoleColor.Gray,
            CommandType.Chain => ConsoleColor.Gray,
            _ => ConsoleColor.White,
        };
    }

    /// <summary>
    /// Formats a token for display: if it looks like a file path, use
    /// <see cref="FilePathDisplayHelper.FormatDisplayPath"/>; otherwise
    /// fall back to truncation for very long tokens.
    /// </summary>
    private static string FormatToken(string token)
    {
        if (LooksLikePath(token))
            return FilePathDisplayHelper.FormatDisplayPath(token);
        return TruncateLong(token);
    }

    /// <summary>
    /// Heuristic: does <paramref name="token"/> look like a file or dir path?
    /// Checks for absolute/home/relative path prefixes or directory separators.
    /// </summary>
    private static bool LooksLikePath(string token)
    {
        if (token.Length == 0) return false;
        if (token[0] == '/' || token[0] == '~') return true;
        if (token.StartsWith("./") || token.StartsWith("../")) return true;
        // Only match '/' if the token doesn't contain brackets (likely markup remnants)
        if (token.Contains('[') || token.Contains(']')) return false;
        return token.Contains('/');
    }

    /// <summary>
    /// Truncates long tokens to a max width, replacing newlines with spaces
    /// and appending "..." when truncated. Tokens under 100 chars pass through.
    /// </summary>
    private static string TruncateLong(string token)
    {
        if (token.Length <= 100) return token;
        var preview = token.Replace('\n', ' ').Replace('\r', ' ');
        if (preview.Length > 97) preview = preview[..97] + "...";
        return preview;
    }
}

using System.Linq;
using OpenClawPTT.Formatting;
using Spectre.Console;

namespace OpenClawPTT.Services;

/// <summary>
/// Console implementation of IToolOutput using StreamShell.
/// Routes all output through the StreamShell host as markup.
/// </summary>
public sealed class ToolOutputHelper : IToolOutput
{
    //private readonly IStreamShellHost _shellHost;

    private readonly AgentReplyFormatter _agentReplayFormatter;
    private readonly StreamShellCapturingConsole _streamShellCapturingConsole;

    private string? _prefix = null;

    /// <summary>
    /// Creates a ToolOutputHelper.
    /// </summary>
    /// <param name="shellHost">StreamShell host for markup output.</param>
    /// <param name="reservedRightMargin">Pre-computed right-edge margin in characters (default 10).</param>
    public ToolOutputHelper(IStreamShellHost shellHost, int reservedRightMargin = 10)
    {
        _streamShellCapturingConsole = new StreamShellCapturingConsole(shellHost);
        _agentReplayFormatter = new AgentReplyFormatter("", reservedRightMargin, prefixAlreadyPrinted: false, output: _streamShellCapturingConsole);

    }

    public void Start(string prefix)
    {
        _prefix = prefix;
        _agentReplayFormatter.Reconfigure(prefix);
    }

    public void Finish()
    {
        _agentReplayFormatter.Finish();
    }

    public void Flush()
    {
        _streamShellCapturingConsole.FlushToStreamShell(_prefix ?? "");
    }

    private void WriteToShell(string text, ConsoleColor color)
    {
        var colorName = ConsoleColorMapper.ToSpectreColor(color);
        
        _agentReplayFormatter.ProcessMarkupDelta($"[{colorName}]{Markup.Escape(text)}[/]");

        //_shellHost.AddMessage($"[{colorName}]{Markup.Escape(text)}[/]");
    }

    public void Print(string text, ConsoleColor color = ConsoleColor.White)
    {
        WriteToShell(text, color);
    }

    public void PrintLine(string text, ConsoleColor color = ConsoleColor.White)
    {
        if (text.Length > 0)
        {
            WriteToShell(text, color);
        }
        // Write newline after the markup tag, not inside it.
        // For empty text, skip WriteToShell entirely to avoid injecting
        // an empty markup pair like "[white][/]" into the output.
        _agentReplayFormatter.ProcessDelta("\n");
    }

    public void PrintMarkup(string markup)
    {
        _agentReplayFormatter.ProcessMarkupDelta(markup);
    }

    public void PrintTruncated(string text, string continuationPrefix, int rightMarginIndent, ConsoleColor color = ConsoleColor.White, int maxRows = 4)
    {
        if (string.IsNullOrEmpty(text)) return;

        var allLines = text.Split('\n');
        var displayLines = allLines.Take(maxRows).ToArray();
        bool hasMore = allLines.Length > maxRows;

        foreach (var line in displayLines)
        {
            if (!string.IsNullOrWhiteSpace(line))
                PrintLine(line, color);
        }
        if (hasMore)
            PrintLine($"... ({allLines.Length - maxRows} more lines)", ConsoleColor.DarkGray);
    }
}

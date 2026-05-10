using OpenClawPTT.Formatting;
using Spectre.Console;

namespace OpenClawPTT.Services;

/// <summary>
/// Handles display of agent "thinking" content based on the configured
/// <see cref="ThinkingMode"/>.
///
/// Modes 2 (Emoji) and 3 (FirstNLines) render tool-output-style headers
/// via <see cref="IToolOutput"/>, reusing the same pipeline as tool calls.
/// Mode 4 (Full) renders through <see cref="AgentReplyFormatter"/> like
/// agent replies, supporting future streaming.
/// </summary>
public sealed class ThinkingDisplayHandler
{
    private readonly AppConfig _config;
    private readonly IStreamShellHost? _shellHost;
    private readonly ToolOutputHelper _toolOutput;

    public ThinkingDisplayHandler(AppConfig config, IStreamShellHost? shellHost)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _shellHost = shellHost;
        _toolOutput = new ToolOutputHelper(shellHost!, _config.ReservedRightMargin);
    }

    public void DisplayThinking(string thinking)
    {
        switch (_config.ThinkingDisplayMode)
        {
            case ThinkingMode.None:
                return;

            case ThinkingMode.Emoji:
                DisplayEmojiOnly();
                return;

            case ThinkingMode.FirstNLines:
                DisplayFirstNLines(thinking);
                return;

            case ThinkingMode.Full:
                DisplayFull(thinking);
                return;
        }
    }

    /// <summary>
    /// Mode 2: Show emoji header with no body — like a tool call that had empty args.
    /// </summary>
    private void DisplayEmojiOnly()
    {
        string header = $"[gray93 on #333333]  💭 Thinking[/] ";
        _shellHost?.AddMessage(header);
        _shellHost?.AddMessage("");
    }

    /// <summary>
    /// Mode 3: Show emoji header + first N lines of thinking, tool-output style.
    /// Breaks thinking text into lines that fit within the console width,
    /// accounting for full-width and half-width character differences,
    /// and respecting <see cref="AppConfig.ThinkingPreviewLines"/>.
    /// </summary>
    private void DisplayFirstNLines(string thinking)
    {
        string emojiHeader = $"[gray93 on #333333]  💭 Thinking[/] ";
        _toolOutput.Start(emojiHeader);

        if (!string.IsNullOrEmpty(thinking))
        {
            // Available width: min of 80 and console width minus overhead
            int consoleWidth = ConsoleHelper.GetWindowWidth();

            // Estimate prefix visual width (~5 chars for "  💭 ", variable for rest)
            int prefixWidth = TextWidth.GetVisualWidth("  💭 Thinking ");
            int maxLineWidth = Math.Min(79, consoleWidth - prefixWidth - _config.ReservedRightMargin);
            if (maxLineWidth < 20) maxLineWidth = 79; // fallback

            var wrappedLines = TextWidth.WrapToVisualWidth(thinking, maxLineWidth);

            var displayLines = wrappedLines.Take(_config.ThinkingPreviewLines).ToList();
            bool hasMore = wrappedLines.Count > _config.ThinkingPreviewLines;

            foreach (var line in displayLines)
            {
                _toolOutput.PrintLine(line, ConsoleColor.Gray);
            }

            if (hasMore)
            {
                int remainingLines = wrappedLines.Count - _config.ThinkingPreviewLines;
                _toolOutput.PrintMarkup($"[dim]... ({remainingLines} more lines)[/]\n");
            }
        }

        _toolOutput.Finish();
        _toolOutput.Flush();
    }

    /// <summary>
    /// Mode 4: Show all thinking through the agent-reply formatting pipeline
    /// (<see cref="AgentReplyFormatter"/> + <see cref="StreamShellCapturingConsole"/>),
    /// enabling word-wrapped output. The text is rendered in gray to match tool
    /// output styling. Ready for future streaming support.
    /// </summary>
    private void DisplayFull(string thinking)
    {
        if (_shellHost == null)
            return;

        var capturingConsole = new StreamShellCapturingConsole(_shellHost);
        string prefix = $"  💭 Thinking: ";
        var formatter = new AgentReplyFormatter(
            prefix,
            _config.ReservedRightMargin,
            prefixAlreadyPrinted: false,
            output: capturingConsole);

        // Render thinking text in gray to match tool output style.
        // Escape brackets so Spectre doesn't interpret them as markup tags,
        // then wrap in gray color tag.
        var escaped = Markup.Escape(thinking);
        formatter.ProcessMarkupDelta($"[gray]{escaped}[/]");
        formatter.Finish();

        capturingConsole.FlushToStreamShell($"[gray93 on #333333]{Markup.Escape(prefix.TrimEnd())}[/]");
    }
}

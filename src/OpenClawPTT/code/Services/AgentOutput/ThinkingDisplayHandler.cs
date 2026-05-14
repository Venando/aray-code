using System;
using System.Linq;
using OpenClawPTT.Formatting;
using OpenClawPTT.Services.Themes;
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
/// All Spectre styles driven from <see cref="ThemeProvider.Current.Tools"/>.
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
        var tools = ThemeProvider.Current.Tools;
        string header = $"[{tools.Thinking.HeaderStyle}]  \U0001f4ad Thinking[/] ";
        _shellHost?.AddMessage(header);
        _shellHost?.AddMessage("");
    }

    /// <summary>
    /// Mode 3: Show emoji header + first N lines of thinking, tool-output style.
    /// </summary>
    private void DisplayFirstNLines(string thinking)
    {
        var tools = ThemeProvider.Current.Tools;
        string emojiHeader = $"[{tools.Thinking.HeaderStyle}]  \U0001f4ad Thinking[/] ";
        _toolOutput.Start(emojiHeader);

        if (!string.IsNullOrEmpty(thinking))
        {
            int consoleWidth = ConsoleMetrics.GetWindowWidth();
            int prefixWidth = CharacterWidth.GetDisplayWidth("  \U0001f4ad Thinking ");
            int maxLineWidth = Math.Min(79, consoleWidth - prefixWidth - _config.ReservedRightMargin);
            if (maxLineWidth < 20) maxLineWidth = 79;

            var wrappedLines = CharacterWidth.WrapToWidth(thinking, maxLineWidth);
            var displayLines = wrappedLines.Take(_config.ThinkingPreviewLines).ToList();
            bool hasMore = wrappedLines.Count > _config.ThinkingPreviewLines;

            foreach (var line in displayLines)
            {
                _toolOutput.PrintLine(line, tools.General.Label);
            }

            if (hasMore)
            {
                int remainingLines = wrappedLines.Count - _config.ThinkingPreviewLines;
                _toolOutput.PrintMarkup($"[{tools.Thinking.MoreStyle}]... ({remainingLines} more lines)[/]\n");
            }
        }

        _toolOutput.Finish();
        _toolOutput.Flush();
    }

    /// <summary>
    /// Mode 4: Show all thinking through the agent-reply formatting pipeline.
    /// </summary>
    private void DisplayFull(string thinking)
    {
        if (_shellHost == null)
            return;

        var tools = ThemeProvider.Current.Tools;
        var capturingConsole = new StreamShellCapturingConsole(_shellHost);
        string prefix = $"  \U0001f4ad Thinking: ";
        var formatter = new AgentReplyFormatter(
            prefix,
            _config.ReservedRightMargin,
            prefixAlreadyPrinted: false,
            output: capturingConsole);

        var escaped = Markup.Escape(thinking);
        formatter.ProcessMarkupDelta($"[{tools.Thinking.TextStyle}]{escaped}[/]");
        formatter.Finish();

        capturingConsole.FlushToStreamShell($"[{tools.Thinking.HeaderStyle}]{Markup.Escape(prefix.TrimEnd())}[/]");
    }
}

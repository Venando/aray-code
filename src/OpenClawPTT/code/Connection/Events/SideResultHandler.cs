using System.Text.Json;
using OpenClawPTT.Services;

namespace OpenClawPTT;

/// <summary>
/// Handles chat.side_result events from the gateway.
/// Displays BTW (by-the-way) side-query results — both successful answers and errors.
/// Handles multi-line text with word-wrapping and proper continuation line prefixes.
/// </summary>
public class SideResultHandler : IEventHandler<SideResultEvent>
{
    private readonly IColorConsole _console;
    private readonly AppConfig? _config;

    public SideResultHandler(IColorConsole console, AppConfig? config = null)
    {
        _console = console;
        _config = config;
    }

    public Task HandleAsync(SideResultEvent evt)
    {
        var payload = evt.Payload;

        var kind = payload.TryGetProperty("kind", out var kindEl)
            ? kindEl.GetString() ?? "unknown" : "unknown";
        var question = payload.TryGetProperty("question", out var qEl)
            ? qEl.GetString() ?? string.Empty : string.Empty;
        var text = payload.TryGetProperty("text", out var textEl)
            ? textEl.GetString() ?? string.Empty : string.Empty;
        var isError = payload.TryGetProperty("isError", out var errEl)
            && errEl.ValueKind == JsonValueKind.True;

        // Calculate available width
        int consoleWidth = ConsoleMetrics.GetWindowWidth();
        int rightMargin = _config?.RightMarginIndent ?? 5;
        if (rightMargin < 1) rightMargin = 5;

        // ── Header ──────────────────────────────────────────────
        _console.PrintMarkup($"[dim]╭─[/] [steelblue1_1]{MarkupEscape(kind)}[/] [dim]side query[/]");

        // ── Question ────────────────────────────────────────────
        if (!string.IsNullOrEmpty(question))
        {
            string prefix = "[dim]│[/] [grey]Q:[/] ";
            int maxLineWidth = consoleWidth - 8 - rightMargin; // "│ Q: " = 6 + 2 padding
            if (maxLineWidth < 20) maxLineWidth = 79;
            foreach (var line in WrapText(question, maxLineWidth))
                _console.PrintMarkup($"{prefix}[white]{MarkupEscape(line)}[/]");
        }

        // ── Answer / Error body ─────────────────────────────────
        if (isError)
        {
            string prefix = "[dim]│[/] [red]✗ ";
            int maxLineWidth = consoleWidth - 6 - rightMargin; // "│ ✗ " = 5
            if (maxLineWidth < 20) maxLineWidth = 79;
            bool first = true;
            foreach (var line in WrapText(text, maxLineWidth))
            {
                if (first)
                {
                    // prefix already contains [red] for ✗, so just close it
                    _console.PrintMarkup($"{prefix}{MarkupEscape(line)}[/]");
                    first = false;
                }
                else
                {
                    _console.PrintMarkup($"[dim]│[/] [red]{MarkupEscape(line)}[/]");
                }
            }
        }
        else if (!string.IsNullOrEmpty(text))
        {
            string prefix = "[dim]│[/] [steelblue1_1]A:[/] ";
            int maxLineWidth = consoleWidth - 8 - rightMargin; // "│ A: " = 6
            if (maxLineWidth < 20) maxLineWidth = 79;
            bool first = true;
            foreach (var line in WrapText(text, maxLineWidth))
            {
                if (first)
                {
                    _console.PrintMarkup($"{prefix}[white]{MarkupEscape(line)}[/]");
                    first = false;
                }
                else
                {
                    _console.PrintMarkup($"[dim]│[/] [white]{MarkupEscape(line)}[/]");
                }
            }
        }

        // ── Footer ─────────────────────────────────────────────
        _console.PrintMarkup($"[dim]╰─[/]");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Splits multi-line text by newlines, then word-wraps each segment
    /// to fit within <paramref name="maxWidth"/> visual characters.
    /// Preserves intentional blank lines.
    /// </summary>
    private static IEnumerable<string> WrapText(string text, int maxWidth)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield return text ?? string.Empty;
            yield break;
        }

        var lines = text.Split('\n');
        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.TrimEnd('\r');
            if (trimmed.Length == 0)
            {
                yield return string.Empty;
                continue;
            }

            var wrapped = CharacterWidth.WrapToWidth(trimmed, maxWidth);
            foreach (var w in wrapped)
                yield return w;
        }
    }

    /// <summary>
    /// Escapes text for Spectre.Console markup to prevent accidental
    /// markup tags in user/agent text from breaking the display.
    /// </summary>
    private static string MarkupEscape(string text)
    {
        return text
            .Replace("[", "[[")
            .Replace("]", "]]");
    }
}

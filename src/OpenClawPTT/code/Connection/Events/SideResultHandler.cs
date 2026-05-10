using System.Text.Json;
using OpenClawPTT.Services;

namespace OpenClawPTT;

/// <summary>
/// Handles chat.side_result events from the gateway.
/// Displays BTW (by-the-way) side-query results — both successful answers and errors.
/// </summary>
public class SideResultHandler : IEventHandler<SideResultEvent>
{
    private readonly IColorConsole _console;

    public SideResultHandler(IColorConsole console)
    {
        _console = console;
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

        // Display the result
        _console.PrintMarkup($"[dim]╭─[/] [steelblue1_1]{kind}[/] [dim]side query[/]");

        if (!string.IsNullOrEmpty(question))
            _console.PrintMarkup($"[dim]│[/] [grey]Q:[/] [white]{MarkupEscape(question)}[/]");

        if (isError)
        {
            _console.PrintMarkup($"[dim]│[/] [red]✗ {MarkupEscape(text)}[/]");
        }
        else if (!string.IsNullOrEmpty(text))
        {
            _console.PrintMarkup($"[dim]│[/] [steelblue1_1]A:[/] [white]{MarkupEscape(text)}[/]");
        }

        _console.PrintMarkup($"[dim]╰─[/]");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Escapes text for Spectre.Console markup to prevent accidental
    /// markup tags in user/agent text from breaking the display.
    /// </summary>
    private static string MarkupEscape(string text)
    {
        // Spectre.Console uses [ and ] for markup tags
        return text
            .Replace("[", "[[")
            .Replace("]", "]]");
    }
}

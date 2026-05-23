using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArayCode.Services;

/// <summary>
/// Handles user input routed through StreamShell's UserInputSubmitted event.
/// Non-command text is sent as a PTT transcription message.
/// This class no longer polls raw console keys — that logic is replaced by
/// StreamShellInputHandler which registers StreamShell command handlers.
/// </summary>
public sealed class InputHandler : IInputHandler
{
    private readonly ITextMessageSender _textSender;

    public InputHandler(ITextMessageSender textSender)
    {
        _textSender = textSender;
    }

    /// <summary>
    /// No-op: input is now handled via StreamShell UserInputSubmitted events
    /// in StreamShellInputHandler. This method exists for backward compatibility with AppLoop.
    /// </summary>
    public Task<InputResult> HandleInputAsync(CancellationToken ct)
    {
        return Task.FromResult(InputResult.Continue);
    }

    /// <summary>Send a text message directly (not from user input).</summary>
    public async Task SendTextAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        await _textSender.SendAsync(text.Trim(), ct);
    }
}

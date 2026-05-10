using OpenClawPTT.Services;
using StreamShell;

namespace OpenClawPTT.Tests;

/// <summary>
/// Testable fake IStreamShellHost that captures messages and allows firing UserInputSubmitted.
/// </summary>
public sealed class FakeStreamShellHost : IStreamShellHost, IDisposable
{
    public readonly List<string> Messages = new();
    public readonly List<StreamShell.Command> Commands = new();

    /// <summary>
    /// Fake input handler that tracks current input text. Supports save/load of input field state.
    /// </summary>
    public StreamShell.IInputHandler InputHandler { get; } = new FakeInputHandler();

    public event Action<StreamShell.UserInputSubmittedEventArgs>? UserInputSubmitted;

    public void AddMessage(string markup) => Messages.Add(markup);

    public void AddCommand(StreamShell.Command command) => Commands.Add(command);

    public Task Run(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Clear() { Messages.Clear(); }

    public string? LastSeparatorLeftText { get; private set; }
    public string? LastSeparatorRightText { get; private set; }

    public void SetTopSeparator(string? leftText = null, string? rightText = null,
        char repeatedCharacter = '\u2500', string? repeatedCharMarkup = null)
    {
        LastSeparatorLeftText = leftText;
        LastSeparatorRightText = rightText;
    }

    public void Stop() { /* no-op */ }
    public void SetRightMarginIndent(int margin) { /* no-op */ }
    public void SetInputPrefix(string prefix) { /* no-op */ }
    public void SetContinuationPrefix(string prefix) { /* no-op */ }

    public void Dispose() { /* no-op */ }

    /// <summary>Simulate user submitting plain text input.</summary>
    public void SubmitInput(string input)
    {
        var args = new StreamShell.UserInputSubmittedEventArgs();
        // Use RawOutput since TextWithoutAttachments may be computed
        var prop = typeof(StreamShell.UserInputSubmittedEventArgs).GetProperty("RawOutput");
        prop?.SetValue(args, input);
        UserInputSubmitted?.Invoke(args);
    }
}

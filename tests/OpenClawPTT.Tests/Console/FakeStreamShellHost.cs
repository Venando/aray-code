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

    public event Action<string, StreamShell.InputType, System.Collections.Generic.IReadOnlyList<StreamShell.Attachment>>? UserInputSubmitted;

    public void AddMessage(string markup) => Messages.Add(markup);

    public void AddCommand(StreamShell.Command command) => Commands.Add(command);

    public Task Run(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Clear() { Messages.Clear(); }

    public void Stop() { /* no-op */ }

    public void Dispose() { /* no-op */ }

    /// <summary>Simulate user submitting plain text input.</summary>
    public void SubmitInput(string input) =>
        UserInputSubmitted?.Invoke(input, StreamShell.InputType.PlainText, Array.Empty<StreamShell.Attachment>());
}

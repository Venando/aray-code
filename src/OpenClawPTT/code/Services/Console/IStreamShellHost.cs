namespace OpenClawPTT.Services;

/// <summary>
/// Testable abstraction over StreamShell's ConsoleAppHost.
/// </summary>
public interface IStreamShellHost
{
    void AddMessage(string markup);
    void AddCommand(StreamShell.Command command);
    void Clear();
    event Action<string, StreamShell.InputType, System.Collections.Generic.IReadOnlyList<StreamShell.Attachment>>? UserInputSubmitted;
    Task Run(CancellationToken cancellationToken = default);
    void Stop();
}

namespace OpenClawPTT.Services;

/// <summary>
/// Testable abstraction over StreamShell's ConsoleAppHost.
/// </summary>
public interface IStreamShellHost
{
    /// <summary>Exposes the input handler for save/load/reset of the input field.</summary>
    StreamShell.IInputHandler InputHandler { get; }

    void AddMessage(string markup);
    void AddCommand(StreamShell.Command command);
    void Clear();
    event Action<StreamShell.UserInputSubmittedEventArgs>? UserInputSubmitted;
    Task Run(CancellationToken cancellationToken = default);
    void Stop();
}

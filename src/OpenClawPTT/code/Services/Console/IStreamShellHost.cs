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

    /// <summary>Sets the right margin indent (in characters) on the underlying StreamShell settings.</summary>
    void SetRightMarginIndent(int margin);

    /// <summary>Sets the input prompt prefix (e.g. " > ") on the underlying StreamShell settings.</summary>
    void SetInputPrefix(string prefix);

    /// <summary>Sets the continuation prefix for wrapped input lines on the underlying StreamShell settings.</summary>
    void SetContinuationPrefix(string prefix);

    /// <summary>Sets the default bottom panel (shown when no command is active).</summary>
    void SetDefaultPanel(StreamShell.IBottomPanel panel);
}

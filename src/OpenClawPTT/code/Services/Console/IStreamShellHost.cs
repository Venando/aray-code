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

    /// <summary>
    /// Sets the top separator line (between message feed and input block).
    /// LeftText/RightText support Spectre markup. Called frequently to update status info.
    /// </summary>
    void SetTopSeparator(string? leftText = null, string? rightText = null,
        char repeatedCharacter = '─', string? repeatedCharMarkup = null);
        
    /// <summary>
    /// Sets the top separator line (between input block and bottom panel).
    /// LeftText/RightTex
    void SetBottomSeparator(string? leftText = null, string? rightText = null,
        char repeatedCharacter = '─', string? repeatedCharMarkup = null);

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

    /// <summary>
    /// Opens an interactive selection panel. Returns selected variants, or null if cancelled.
    /// </summary>
    Task<StreamShell.IVariant[]?> PromptSelection(string title, StreamShell.IVariant[] variants, StreamShell.SelectionInfo? info = null);
}

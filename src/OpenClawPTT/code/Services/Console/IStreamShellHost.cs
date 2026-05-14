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

    /// <summary>Adds a command by its constituent parts (convenience overload).</summary>
    void AddCommand(string name, string description,
        Func<string[], Dictionary<string, string>, Task> handler,
        string[]? argumentSuggestions = null);

    /// <summary>Removes a previously registered command by name.</summary>
    void RemoveCommand(string name);

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
    event EventHandler<StreamShell.BottomPanelChangedEventArgs>? BottomPanelChanged;
    Task Run(CancellationToken cancellationToken = default);
    void Stop();

    /// <summary>Sets the right margin indent (in characters) on the underlying StreamShell settings.</summary>
    void SetRightMarginIndent(int margin);

    /// <summary>Sets the input prompt prefix (e.g. " > ") on the underlying StreamShell settings.</summary>
    void SetInputPrefix(string prefix);

    /// <summary>Sets the continuation prefix for wrapped input lines on the underlying StreamShell settings.</summary>
    void SetContinuationPrefix(string prefix);

    /// <summary>Sets the cursor highlight markup on the underlying StreamShell settings.</summary>
    void SetCursorMarkup(string markup);

    /// <summary>Sets the selection highlight markup on the underlying StreamShell settings.</summary>
    void SetSelectionMarkup(string markup);

    /// <summary>Sets the command slash character markup on the underlying StreamShell settings.</summary>
    void SetCommandSlashMarkup(string markup);

    /// <summary>Applies all theme-driven settings (cursor, selection, command slash, prefix) from ThemeConfig.</summary>
    void ApplyStreamShellTheme();

    /// <summary>Sets the default bottom panel (shown when no command is active).</summary>
    void SetDefaultPanel(StreamShell.IBottomPanel panel);

    /// <summary>Sets a temporary bottom panel (overrides the default until ResetBottomPanel is called).</summary>
    void SetBottomPanel(StreamShell.IBottomPanel panel);

    /// <summary>Resets to the default bottom panel after a temporary override.</summary>
    void ResetBottomPanel();

    /// <summary>
    /// Opens an interactive selection panel. Returns selected variants, or null if cancelled.
    /// </summary>
    Task<StreamShell.IVariant[]?> PromptSelection(string title, StreamShell.IVariantEntry[] variants, StreamShell.SelectionInfo? info = null);
}

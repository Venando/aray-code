using OpenClawPTT.Services.Themes;
using StreamShell;

namespace OpenClawPTT.Services;

/// <summary>
/// Singleton wrapper around StreamShell's ConsoleAppHost.
/// Exposes AddMessage, command registration, and the UserInputSubmitted event.
/// </summary>
public sealed class StreamShellHost : IStreamShellHost, IDisposable
{
    private readonly ConsoleAppHost _host;
    // Tracks commands added by overload shortcuts so RemoveCommand can overwrite them.
    // ConsoleAppHost itself has no RemoveCommand — we replace with a no-op.
    private readonly HashSet<string> _trackedCommands = new();

    public StreamShellHost()
    {
        _host = new ConsoleAppHost();
    }

    /// <summary>Exposes the input handler for save/load/reset of the input field.</summary>
    public StreamShell.IInputHandler InputHandler => _host.InputHandler;

    public void AddMessage(string markup) => _host.AddMessage(markup);

    public void AddCommand(StreamShell.Command command) => _host.AddCommand(command);

    public void AddCommand(string name, string description,
        Func<string[], Dictionary<string, string>, Task> handler,
        string[]? argumentSuggestions = null)
    {
        _host.AddCommand(name, description, handler, argumentSuggestions ?? []);
        _trackedCommands.Add(name);
    }

    public void RemoveCommand(string name)
    {
        if (_trackedCommands.Remove(name))
        {
            // ConsoleAppHost has no RemoveCommand, so overwrite with a no-op handler.
            _host.AddCommand(name, string.Empty, (_, _) => Task.CompletedTask, []);
        }
    }

    public void SetRenderChunkSize(int size)
    {
        _host.Settings.PrintingMode = MessagePrintingMode.IntChunks;
        _host.Settings.RenderChunkSize = size;
    }
    
    public void SetExpDecayRate(double expDecayrate)
    {
        _host.Settings.PrintingMode = MessagePrintingMode.ExpDecay;
        _host.Settings.ExpDecayRate = expDecayrate;
    }

    public event Action<StreamShell.UserInputSubmittedEventArgs>? UserInputSubmitted
    {
        add => _host.UserInputSubmitted += value;
        remove => _host.UserInputSubmitted -= value;
    }

    public event EventHandler<StreamShell.BottomPanelChangedEventArgs>? BottomPanelChanged
    {
        add => _host.BottomPanelChanged += value;
        remove => _host.BottomPanelChanged -= value;
    }

    public async Task Run(CancellationToken cancellationToken = default) => await _host.Run(cancellationToken);

    public void Clear()
    {
        // Clear the terminal screen using ANSI escape code
        Spectre.Console.AnsiConsole.Clear();
    }

    /// <summary>Sets the top separator line (between message feed and input block).</summary>
    public void SetTopSeparator(string? leftText = null, string? rightText = null,
        char repeatedCharacter = '\u2500', string? repeatedCharMarkup = null)
    {
        _host.SetTopSeparator(leftText, rightText, repeatedCharacter, repeatedCharMarkup);
    }

    /// <summary>Sets the bottom separator line (between input block and bottom panel).</summary>
    public void SetBottomSeparator(string? leftText = null, string? rightText = null,
        char repeatedCharacter = '\u2500', string? repeatedCharMarkup = null)
    {
        _host.SetBottomSeparator(leftText, rightText, repeatedCharacter, repeatedCharMarkup);
    }
    
    public void Stop() => _host.Stop();

    public void SetRightMarginIndent(int margin) => _host.Settings.WrappingRightMargin = margin;
    public void SetInputPrefix(string prefix) => _host.Settings.InputPrefix = prefix;
    public void SetContinuationPrefix(string prefix) => _host.Settings.ContinuationPrefix = prefix;
    public void SetCursorMarkup(string markup) => _host.Settings.CursorMarkup = markup;
    public void SetSelectionMarkup(string markup) => _host.Settings.SelectionMarkup = markup;
    public void SetCommandSlashMarkup(string markup) => _host.Settings.CommandSlashMarkup = markup;

    public void ApplyStreamShellTheme(int prefixWidth)
    {
        var t = ThemeProvider.Current.Tools;
        var p = ThemeProvider.Current.Palette;

        _host.Settings.CursorMarkup = t.StreamShell.CursorMarkup;
        _host.Settings.SelectionMarkup = t.StreamShell.SelectionMarkup;
        _host.Settings.CommandSlashMarkup = t.StreamShell.CommandSlashMarkup;

        // Build input prefix: theme style + dynamic spacing to match user message width
        int wsCount = Math.Max(0, prefixWidth - 2);
        _host.Settings.InputPrefix = $"[{t.StreamShell.InputPrefixStyle}]{new string(' ', wsCount)}> [/]";
        _host.Settings.ContinuationPrefix = new string(' ', prefixWidth);

        // Apply palette
        _host.Settings.Palette.SelectedStyle = p.SelectedBackground;
        _host.Settings.Palette.SelectedCursorColor = p.SelectedCursorColor;
        _host.Settings.Palette.SelectedCursorSymbol = p.SelectedCursorSymbol;
        _host.Settings.Palette.SelectedNameColor = p.SelectedNameColor;
        _host.Settings.Palette.SelectedDescriptionColor = p.SelectedDescriptionColor;
        _host.Settings.Palette.NormalIndent = p.NormalIndent;
        _host.Settings.Palette.NormalNameColor = p.NormalNameColor;
    }
    public void SetDefaultPanel(StreamShell.IBottomPanel panel) => _host.SetDefaultPanel(panel);

    public void SetBottomPanel(StreamShell.IBottomPanel panel) => _host.SetBottomPanel(panel);

    public void ResetBottomPanel() => _host.ResetBottomPanel();

    public Task<StreamShell.IVariant[]?> PromptSelection(string title, StreamShell.IVariantEntry[] variants, StreamShell.SelectionInfo? info = null)
        => _host.PromptSelection(title, variants, info);

    public void Dispose() => _host.Dispose();
}

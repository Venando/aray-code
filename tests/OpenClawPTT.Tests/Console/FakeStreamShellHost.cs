using OpenClawPTT.Services;
using OpenClawPTT.Services.Commands;
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

    public void AddCommand(string name, string description,
        Func<string[], Dictionary<string, string>, Task> handler,
        string[]? argumentSuggestions = null)
    {
        Commands.Add(new StreamShell.Command(name, description, handler, argumentSuggestions ?? []));
    }

    public void RemoveCommand(string name) { /* no-op */ }

    public Task Run(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Clear() { Messages.Clear(); }

    public string? LastTopSeparatorLeftText { get; private set; }
    public string? LastTopSeparatorRightText { get; private set; }
    public string? LastBottomSeparatorLeftText { get; private set; }
    public string? LastBottomSeparatorRightText { get; private set; }
    public string? LastSeparatorLeftText => LastTopSeparatorLeftText;
    public string? LastSeparatorRightText => LastTopSeparatorRightText;

    public void SetTopSeparator(string? leftText = null, string? rightText = null,
        char repeatedCharacter = '\u2500', string? repeatedCharMarkup = null)
    {
        LastTopSeparatorLeftText = leftText;
        LastTopSeparatorRightText = rightText;
    }

    public void SetBottomSeparator(string? leftText = null, string? rightText = null,
        char repeatedCharacter = '\u2500', string? repeatedCharMarkup = null)
    {
        LastBottomSeparatorLeftText = leftText;
        LastBottomSeparatorRightText = rightText;
    }

    public void Stop() { /* no-op */ }
    public void SetRightMarginIndent(int margin) { /* no-op */ }
    public void SetInputPrefix(string prefix) { /* no-op */ }
    public void SetContinuationPrefix(string prefix) { /* no-op */ }
    public void SetDefaultPanel(IBottomPanel panel) { /* no-op */ }
    public void SetPanelHistoryService(SessionHistoryService service) { /* no-op */ }
    public void SetDefaultPanelFactory(Func<IBottomPanel> factory) { /* no-op */ }
    public void ResetDefaultPanel() { /* no-op */ }
    public void SetBottomPanel(IBottomPanel panel) { /* no-op */ }
    public void ResetBottomPanel() { /* no-op */ }

    public event EventHandler<BottomPanelChangedEventArgs>? BottomPanelChanged { add { } remove { } }

    public Task<IVariant[]?> PromptSelection(string title, IVariantEntry[] variants, SelectionInfo? info = null)
        => Task.FromResult<IVariant[]?>(null);

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

    public void SetRenderChunkSize(int size) { }
    public void SetCursorMarkup(string markup) { }
    public void SetSelectionMarkup(string markup) { }
    public void SetCommandSlashMarkup(string markup) { }
    public void ApplyStreamShellTheme(int prefixWidth) { }
}

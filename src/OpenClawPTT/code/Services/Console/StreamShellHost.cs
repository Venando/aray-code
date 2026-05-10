using StreamShell;

namespace OpenClawPTT.Services;

/// <summary>
/// Singleton wrapper around StreamShell's ConsoleAppHost.
/// Exposes AddMessage, command registration, and the UserInputSubmitted event.
/// </summary>
public sealed class StreamShellHost : IStreamShellHost, IDisposable
{
    private readonly ConsoleAppHost _host;

    public StreamShellHost()
    {
        _host = new ConsoleAppHost();
    }

    /// <summary>Exposes the input handler for save/load/reset of the input field.</summary>
    public StreamShell.IInputHandler InputHandler => _host.InputHandler;

    public void AddMessage(string markup) => _host.AddMessage(markup);

    public void AddCommand(StreamShell.Command command) => _host.AddCommand(command);

    public event Action<StreamShell.UserInputSubmittedEventArgs>? UserInputSubmitted
    {
        add => _host.UserInputSubmitted += value;
        remove => _host.UserInputSubmitted -= value;
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

    public void Stop() => _host.Stop();

    public void SetRightMarginIndent(int margin) => _host.Settings.WrappingRightMargin = margin;
    public void SetInputPrefix(string prefix) => _host.Settings.InputPrefix = prefix;
    public void SetContinuationPrefix(string prefix) => _host.Settings.ContinuationPrefix = prefix;

    public void Dispose() => _host.Dispose();
}

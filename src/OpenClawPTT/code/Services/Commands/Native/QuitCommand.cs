namespace OpenClawPTT.Services.Commands;

/// <summary>Native command: /quit — exits the application.</summary>
public sealed class QuitCommand : ICommand
{
    private readonly IStreamShellHost _host;
    private readonly Action _onQuit;

    public string Name => "quit";
    public string Description => "Exit the application";
    public CommandSource Source => CommandSource.Native;
    public ShellCommandType Type => ShellCommandType.System;
    public string[]? Suggestions => null;

    public QuitCommand(IStreamShellHost host, Action onQuit)
    {
        _host = host;
        _onQuit = onQuit;
    }

    public Task ExecuteAsync(string[] args, Dictionary<string, string> namedArgs, CancellationToken ct = default)
    {
        _host.AddMessage("[green]  Bye![/]");
        _onQuit();
        return Task.CompletedTask;
    }
}

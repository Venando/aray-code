namespace ArayCode.Services.Commands;

/// <summary>Native command: /clean — clears the terminal screen.</summary>
public sealed class CleanCommand : ICommand
{
    private readonly IStreamShellHost _host;

    public string Name => "clean";
    public string Description => "Clear the terminal screen";
    public CommandSource Source => CommandSource.Native;
    public ShellCommandType Type => ShellCommandType.System;
    public string[]? Suggestions => null;

    public CleanCommand(IStreamShellHost host)
    {
        _host = host;
    }

    public Task ExecuteAsync(string[] args, Dictionary<string, string> namedArgs, CancellationToken ct = default)
    {
        _host.Clear();
        return Task.CompletedTask;
    }
}

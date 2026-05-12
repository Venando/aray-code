using Spectre.Console;

namespace OpenClawPTT.Services.Commands;

/// <summary>Native command: /history — loads and displays recent session history.</summary>
public sealed class HistoryCommand : ICommand
{
    private readonly IStreamShellHost _host;
    private readonly IGatewayService _gatewayService;
    private readonly IColorConsole _console;
    private readonly IPttStateMachine _pttStateMachine;
    private readonly AppConfig _appConfig;

    public string Name => "history";
    public string Description => "[[N]] Load N session history entries";
    public CommandSource Source => CommandSource.Native;
    public CommandType Type => CommandType.History;
    public string[]? Suggestions => null;

    public HistoryCommand(
        IStreamShellHost host,
        IGatewayService gatewayService,
        IColorConsole console,
        IPttStateMachine pttStateMachine,
        AppConfig appConfig)
    {
        _host = host;
        _gatewayService = gatewayService;
        _console = console;
        _pttStateMachine = pttStateMachine;
        _appConfig = appConfig;
    }

    public async Task ExecuteAsync(string[] args, Dictionary<string, string> namedArgs, CancellationToken ct = default)
    {
        var sessionKey = AgentRegistry.ActiveSessionKey;
        if (sessionKey == null)
        {
            _host.AddMessage("[yellow]  No active session.[/]");
            return;
        }

        int limit = _appConfig.HistoryDisplayCount;
        if (args.Length > 0 && int.TryParse(args[0], out var requested))
            limit = Math.Clamp(requested, 1, 200);

        var history = await _gatewayService.FetchSessionHistoryAsync(sessionKey, limit);
        if (history == null || history.Count == 0)
        {
            _host.AddMessage("[yellow]  No history entries found.[/]");
            return;
        }

        _pttStateMachine.DuringReplay = true;
        try
        {
            _host.AddMessage($"  [grey]── history ({history.Count} entries) ──[/]");
            foreach (var entry in history)
            {
                if (entry.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                    _console.PrintUserMessage(entry.Content);
                else
                    _gatewayService.DisplayHistoryEntry(entry);
            }
        }
        finally
        {
            _pttStateMachine.DuringReplay = false;
        }
    }
}

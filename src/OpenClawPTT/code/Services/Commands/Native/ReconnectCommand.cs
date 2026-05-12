using OpenClawPTT.Services.Diagnostics;
using Spectre.Console;

namespace OpenClawPTT.Services.Commands;

/// <summary>Native command: /reconnect — attempts to reconnect to the gateway.</summary>
public sealed class ReconnectCommand : ICommand
{
    private readonly IStreamShellHost _host;
    private readonly IGatewayService _gatewayService;
    private readonly IColorConsole _console;
    private readonly IStatusService _statusService;
    private readonly ErrorLogStore _errorLog;
    private readonly SessionHistoryService _historyService;

    public string Name => "reconnect";
    public string Description => "Reconnect to the gateway";
    public CommandSource Source => CommandSource.Native;
    public CommandType Type => CommandType.GatewayControl;
    public string[]? Suggestions => null;

    public ReconnectCommand(
        IStreamShellHost host,
        IGatewayService gatewayService,
        IColorConsole console,
        IStatusService statusService,
        ErrorLogStore errorLog,
        SessionHistoryService historyService)
    {
        _host = host;
        _gatewayService = gatewayService;
        _console = console;
        _statusService = statusService;
        _errorLog = errorLog;
        _historyService = historyService;
    }

    public async Task ExecuteAsync(string[] args, Dictionary<string, string> namedArgs, CancellationToken ct = default)
    {
        _statusService.SetGatewayStatus("Reconnecting", StatusColor.Yellow);
        _host.AddMessage("[cyan2]  Attempting to reconnect to gateway...[/]");
        try
        {
            await _gatewayService.ConnectAsync(ct);
            _statusService.SetGatewayStatus("Connected", StatusColor.Green);
            _host.AddMessage("[green]  Reconnected successfully.[/]");

            var sessionKey = AgentRegistry.ActiveSessionKey;
            if (sessionKey != null)
                await _historyService.PrintSessionHistoryAsync(sessionKey, ct);
        }
        catch (Exception ex)
        {
            _statusService.SetGatewayStatus("Disconnected", StatusColor.Red);
            var classification = GatewayErrorClassifier.Classify(ex);
            _errorLog.Write(classification.ToLogEntry());
            _host.AddMessage($"[red]  Reconnect failed: {classification.HumanMessage}[/]");
            if (classification.SuggestedActions.Length > 0)
            {
                _host.AddMessage("[grey]  Suggested actions:[/]");
                foreach (var action in classification.SuggestedActions)
                    _host.AddMessage($"    → [grey]{Markup.Escape(action)}[/]");
            }
        }
    }
}

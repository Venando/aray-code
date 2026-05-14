using OpenClawPTT.Services.Diagnostics;
using OpenClawPTT.Services.Themes;
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

    /// <summary>
    /// Optional callback invoked after a successful reconnection.
    /// Used by StreamShellInputHandler to re-register gateway-dependent commands.
    /// </summary>
    public Func<Task>? OnReconnectSuccess { get; set; }

    public string Name => "reconnect";
    public string Description => "Reconnect to the gateway";
    public CommandSource Source => CommandSource.Native;
    public ShellCommandType Type => ShellCommandType.GatewayControl;
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
        _statusService.SetServiceStatus(ServiceKind.Gateway, StatusColor.Yellow);
        _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Highlight}]  Attempting to reconnect to gateway...[/]");
        try
        {
            await _gatewayService.ConnectAsync(ct);
            _statusService.SetServiceStatus(ServiceKind.Gateway, StatusColor.Green);
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Success}]  Reconnected successfully.[/]");

            // Notify subscribers that gateway is back
            if (OnReconnectSuccess != null)
                await OnReconnectSuccess();

            var sessionKey = AgentRegistry.ActiveSessionKey;
            if (sessionKey != null)
                await _historyService.PrintSessionHistoryAsync(sessionKey, ct);
        }
        catch (Exception ex)
        {
            _statusService.SetServiceStatus(ServiceKind.Gateway, StatusColor.Red);
            var classification = GatewayErrorClassifier.Classify(ex);
            _errorLog.Write(classification.ToLogEntry());
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Error}]  Reconnect failed: {classification.HumanMessage}[/]");
            if (classification.SuggestedActions.Length > 0)
            {
                _host.AddMessage($"[{ThemeProvider.Current.Tools.General.Muted}]  Suggested actions:[/]");
                foreach (var action in classification.SuggestedActions)
                    _host.AddMessage($"    → [{ThemeProvider.Current.Tools.General.Muted}]{Markup.Escape(action)}[/]");
            }
        }
    }
}

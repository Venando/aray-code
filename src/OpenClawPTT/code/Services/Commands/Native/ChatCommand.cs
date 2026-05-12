using System.Linq;
using OpenClawPTT.ConfigWizard;
using Spectre.Console;

namespace OpenClawPTT.Services.Commands;

/// <summary>Native command: /chat — switches active agent and prints recent history.</summary>
public sealed class ChatCommand : ICommand
{
    private readonly IStreamShellHost _host;
    private readonly IConfigurationService _configService;
    private readonly SessionHistoryService _historyService;
    private readonly AppConfig _appConfig;

    public string Name => "chat";
    public string Description => "<name|id> Switch active agent by name or ID";
    public CommandSource Source => CommandSource.Native;
    public ShellCommandType Type => ShellCommandType.AgentManagement;
    public string[]? Suggestions => null;

    public ChatCommand(
        IStreamShellHost host,
        IConfigurationService configService,
        SessionHistoryService historyService,
        AppConfig appConfig)
    {
        _host = host;
        _configService = configService;
        _historyService = historyService;
        _appConfig = appConfig;
    }

    public async Task ExecuteAsync(string[] args, Dictionary<string, string> namedArgs, CancellationToken ct = default)
    {
        // Reject agent switching while a wizard is active
        if (WizardState.IsActive)
        {
            _host.AddMessage("[yellow]  Cannot switch agents during configuration.[/]");
            return;
        }

        if (args.Length == 0)
        {
            _host.AddMessage("[yellow]  Usage: /chat <name|id>[/]");
            return;
        }

        var search = string.Join(" ", args);
        var matched = AgentRegistry.Agents.FirstOrDefault(a =>
            a.Name.Equals(search, StringComparison.OrdinalIgnoreCase) ||
            a.AgentId.Equals(search, StringComparison.OrdinalIgnoreCase));

        if (matched == null)
        {
            _host.AddMessage($"[red]  Agent not found: {Markup.Escape(search)}[/]");
            return;
        }

        if (AgentRegistry.SetActiveAgent(matched.AgentId))
        {
            _appConfig.LastActiveAgentId = matched.AgentId;
            _configService.Save(_appConfig);
            await _historyService.PrintSessionHistoryAsync(matched.SessionKey, ct);
        }
        else
        {
            _host.AddMessage("[yellow]  That agent is already active.[/]");
        }
    }
}

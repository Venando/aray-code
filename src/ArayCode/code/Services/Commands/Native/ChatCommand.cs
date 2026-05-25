using System.Linq;
using ArayCode.ConfigWizard;
using ArayCode.Services.Themes;
using Spectre.Console;

namespace ArayCode.Services.Commands;

/// <summary>Native command: /chat — switches active agent and prints recent history.</summary>
public sealed class ChatCommand : ICommand
{
    private readonly IStreamShellHost _host;
    private readonly IConfigurationService _configService;
    private readonly SessionHistoryService _historyService;

    public string Name => "chat";
    public string Description => "<name|id> Switch active agent by name or ID";
    public CommandSource Source => CommandSource.Native;
    public ShellCommandType Type => ShellCommandType.AgentManagement;
    public string[]? Suggestions => null;

    public ChatCommand(
        IStreamShellHost host,
        IConfigurationService configService,
        SessionHistoryService historyService)
    {
        _host = host;
        _configService = configService;
        _historyService = historyService;
    }

    public async Task ExecuteAsync(string[] args, Dictionary<string, string> namedArgs, CancellationToken ct = default)
    {
        // Reject agent switching while a wizard is active
        if (WizardState.IsActive)
        {
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  Cannot switch agents during configuration.[/]");
            return;
        }

        if (args.Length == 0)
        {
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  Usage: /chat <name|id>[/]");
            return;
        }

        var search = string.Join(" ", args);
        var matched = AgentRegistry.Agents.FirstOrDefault(a =>
            a.Name.Equals(search, StringComparison.OrdinalIgnoreCase) ||
            a.AgentId.Equals(search, StringComparison.OrdinalIgnoreCase));

        if (matched == null)
        {
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Error}]  Agent not found: {Markup.Escape(search)}[/]");
            return;
        }

        // Avoid no-op: check if already active before switching
        if (AgentRegistry.ActiveAgentId?.Equals(matched.AgentId, StringComparison.OrdinalIgnoreCase) == true)
        {
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  That agent is already active.[/]");
            return;
        }

        await AgentRegistry.SwitchToAgentAsync(matched.AgentId, _configService, _historyService, ct);
    }
}

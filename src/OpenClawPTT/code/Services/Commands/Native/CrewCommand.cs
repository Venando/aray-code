using System.Linq;
using OpenClawPTT.Services.Themes;
using Spectre.Console;

namespace OpenClawPTT.Services.Commands;

/// <summary>Native command: /crew — lists agents or starts the config wizard.</summary>
public sealed class CrewCommand : ICommand
{
    private readonly IStreamShellHost _host;
    private readonly IAgentSettingsPersistence _agentSettingsPersistence;
    private readonly AppConfig _appConfig;
    private readonly SessionHistoryService _historyService;
    private readonly IConfigurationService _configService;

    public string Name => "crew";
    public string Description => "List available agents. \"/crew config\" to config";
    public CommandSource Source => CommandSource.Native;
    public ShellCommandType Type => ShellCommandType.AgentManagement;
    public string[]? Suggestions => null;

    public CrewCommand(
        IStreamShellHost host,
        IAgentSettingsPersistence agentSettingsPersistence,
        AppConfig appConfig,
        SessionHistoryService historyService,
        IConfigurationService configService)
    {
        _host = host;
        _agentSettingsPersistence = agentSettingsPersistence;
        _appConfig = appConfig;
        _historyService = historyService;
        _configService = configService;
    }

    public Task ExecuteAsync(string[] args, Dictionary<string, string> namedArgs, CancellationToken ct = default)
    {
        if (args.Length > 0 && args[0].Equals("config", StringComparison.OrdinalIgnoreCase))
            return HandleConfigAsync(args.Skip(1).ToArray(), ct);

        return HandleCrewAsync();
    }

    private Task HandleCrewAsync()
    {
        var agents = AgentRegistry.Agents;
        var activeKey = AgentRegistry.ActiveSessionKey;

        if (agents.Count == 0)
        {
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  No agents available. Make sure you're connected.[/]");
            return Task.CompletedTask;
        }

        var globalHotkey = _appConfig.HotkeyCombination;
        var allSettings = _agentSettingsPersistence.AllAgentSettings;

        _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Highlight}]  Available agents:[/]");
        foreach (var entry in allSettings)
        {
            var isActive = entry.Agent.SessionKey == activeKey;
            var marker = isActive ? " ►" : "  ";
            var emojiDisplay = entry.Emoji ?? "🤖";
            var effectiveColor = entry.Color ?? AgentPersistedSettings.DefaultColor;
            var colorTag = $"[{effectiveColor}]";
            var colorClose = "[/]";
            var nameDisplay = $"{colorTag}{Markup.Escape(entry.Agent.Name)}{colorClose}";
            var hotkeyDisplay = entry.Hotkey != null
                ? Markup.Escape(entry.Hotkey)
                : $"[{ThemeProvider.Current.Tools.General.Muted}](global: {Markup.Escape(globalHotkey)})[/]";
            var colorValueDisplay = $"{colorTag}{Markup.Escape(effectiveColor)}{colorClose}";
            if (entry.Color == null)
                colorValueDisplay += $" [{ThemeProvider.Current.Tools.General.Muted}](default)[/]";

            var showStatus = entry.ShowInStatusPanel ? "" : $" [{ThemeProvider.Current.Tools.General.Muted}](hidden)[/]";

            _host.AddMessage($"  {marker} {emojiDisplay} [{ThemeProvider.Current.Tools.Messages.Emphasis}]{nameDisplay}[/] [{ThemeProvider.Current.Tools.General.Muted}]({Markup.Escape(entry.Agent.AgentId)})[/] — hotkey: {hotkeyDisplay}, emoji: {Markup.Escape(emojiDisplay)}, color: {colorValueDisplay}{showStatus}");
        }
        _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Info}]  Use /crew config for interactive setup[/]");
        return Task.CompletedTask;
    }

    private Task HandleConfigAsync(string[] args, CancellationToken ct)
    {
        AgentRegistry.Deactivate();

        AgentInfo? matched = null;
        if (args.Length > 0)
        {
            var search = string.Join(" ", args);
            matched = AgentRegistry.Agents.FirstOrDefault(a =>
                a.Name.Equals(search, StringComparison.OrdinalIgnoreCase) ||
                a.AgentId.Equals(search, StringComparison.OrdinalIgnoreCase));

            if (matched == null)
            {
                _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Error}]  Agent not found: {Markup.Escape(search)}[/]");
                return Task.CompletedTask;
            }
        }
        else
        {
            var agents = AgentRegistry.Agents;
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Highlight}]  Select an agent to configure:[/]");
            foreach (var agent in agents)
            {
                var emoji = _agentSettingsPersistence.GetPersistedEmoji(agent.AgentId) ?? "🤖";
                var color = _agentSettingsPersistence.GetPersistedColor(agent.AgentId);
                var nameStr = color != null ? $"[{color}]{Markup.Escape(agent.Name)}[/]" : Markup.Escape(agent.Name);
                _host.AddMessage($"  {emoji} {nameStr} [{ThemeProvider.Current.Tools.General.Muted}]({Markup.Escape(agent.AgentId)})[/]");
            }
            _host.AddMessage("");
        }

        var wizard = new AgentConfigWizard(_host, _agentSettingsPersistence);
        wizard.OnConfigured = agent =>
        {
            _ = _historyService.ActivateWithHistoryAsync(agent, _configService, ct);
        };
        _ = wizard.RunAsync(matched);
        return Task.CompletedTask;
    }
}

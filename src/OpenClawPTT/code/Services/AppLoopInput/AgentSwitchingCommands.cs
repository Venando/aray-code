using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using OpenClawPTT.Services.Diagnostics;
using Spectre.Console;

namespace OpenClawPTT;

/// <summary>
/// Handles StreamShell commands for agent listing and switching:
/// /crew, /chat, session history, and OpenClaw tool command forwarding.
/// Extracted from StreamShellInputHandler to honor Single Responsibility.
/// </summary>
public sealed class AgentSwitchingCommands
{
    private readonly IStreamShellHost _host;
    private readonly ITextMessageSender _textSender;
    private readonly IGatewayService _gatewayService;
    private readonly AppConfig _appConfig;
    private readonly IColorConsole _console;
    private readonly IAgentSettingsPersistence _agentSettingsPersistence;
    private readonly IPttStateMachine _pttStateMachine;
    private readonly ErrorLogStore _errorLog;

    public AgentSwitchingCommands(IStreamShellHost host, ITextMessageSender textSender, IGatewayService gatewayService, AppConfig appConfig, IColorConsole console, IAgentSettingsPersistence agentSettingsPersistence, IPttStateMachine pttStateMachine, ErrorLogStore errorLog)
    {
        _host = host;
        _textSender = textSender;
        _gatewayService = gatewayService;
        _appConfig = appConfig;
        _console = console;
        _agentSettingsPersistence = agentSettingsPersistence;
        _pttStateMachine = pttStateMachine;
        _errorLog = errorLog;
    }

    /// <summary>Handler for /crew — lists available agents with all settings.</summary>
    public Task HandleCrew(string[] args)
    {
        var agents = AgentRegistry.Agents;
        var activeKey = AgentRegistry.ActiveSessionKey;

        if (agents.Count == 0)
        {
            _host.AddMessage("[yellow]  No agents available. Make sure you're connected.[/]");
            return Task.CompletedTask;
        }

        var globalHotkey = _appConfig.HotkeyCombination;
        var allSettings = _agentSettingsPersistence.AllAgentSettings;

        _host.AddMessage("[cyan2]  Available agents:[/]");
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
                : $"[grey](global: {Markup.Escape(globalHotkey)})[/]";
            var colorValueDisplay = $"{colorTag}{Markup.Escape(effectiveColor)}{colorClose}";
            if (entry.Color == null)
                colorValueDisplay += " [grey](default)[/]";

            _host.AddMessage($"  {marker} {emojiDisplay} [bold]{nameDisplay}[/] [grey]({Markup.Escape(entry.Agent.AgentId)})[/] — hotkey: {hotkeyDisplay}, emoji: {Markup.Escape(emojiDisplay)}, color: {colorValueDisplay}");
        }
        _host.AddMessage("[grey]  Use /crew config for interactive setup[/]");
        return Task.CompletedTask;
    }

    /// <summary>Handler for /crew config — interactive agent configuration wizard.</summary>
    public Task HandleConfigCommand(string[] args)
    {
        // Deactivate the current agent so messages don't interfere with the wizard
        AgentRegistry.Deactivate();

        if (args.Length > 0)
        {
            // Resolve agent name/ID and skip the agent selection step
            var search = string.Join(" ", args);
            var matched = AgentRegistry.Agents.FirstOrDefault(a =>
                a.Name.Equals(search, StringComparison.OrdinalIgnoreCase) ||
                a.AgentId.Equals(search, StringComparison.OrdinalIgnoreCase));

            if (matched == null)
            {
                _host.AddMessage($"[red]  Agent not found: {Markup.Escape(search)}[/]");
                return Task.CompletedTask;
            }

            var wizard = new AgentConfigWizard(_host, _agentSettingsPersistence);
            wizard.OnConfigured = agent =>
            {
                AgentRegistry.SetActiveAgent(agent.AgentId);
                _ = ActivateWithHistoryAsync(agent);
            };
            _ = wizard.RunAsync(matched);
            return Task.CompletedTask;
        }

        // Show agent list, then start the wizard with agent selection step
        var agents = AgentRegistry.Agents;
        _host.AddMessage("[cyan2]  Select an agent to configure:[/]");
        foreach (var agent in agents)
        {
            var emoji = _agentSettingsPersistence.GetPersistedEmoji(agent.AgentId) ?? "🤖";
            var color = _agentSettingsPersistence.GetPersistedColor(agent.AgentId);
            var nameStr = color != null ? $"[{color}]{Markup.Escape(agent.Name)}[/]" : Markup.Escape(agent.Name);
            _host.AddMessage($"  {emoji} {nameStr} [grey]({Markup.Escape(agent.AgentId)})[/]");
        }
        _host.AddMessage("");

        var wizard2 = new AgentConfigWizard(_host, _agentSettingsPersistence);
        wizard2.OnConfigured = agent =>
        {
            AgentRegistry.SetActiveAgent(agent.AgentId);
            _ = ActivateWithHistoryAsync(agent);
        };
        _ = wizard2.RunAsync();
        return Task.CompletedTask;
    }

    /// <summary>Handler for /chat — switches active agent and prints recent history.</summary>
    public async Task HandleChat(string[] args)
    {
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
            await PrintSessionHistory(matched.SessionKey);
            _console.PrintAgentIntroduction(_appConfig);
        }
        else
        {
            _host.AddMessage("[yellow]  That agent is already active.[/]");
        }
    }

    /// <summary>Reusable helper: pull session history then show agent introduction banner.</summary>
    public async Task ActivateWithHistoryAsync(AgentInfo agent)
    {
        AgentRegistry.SetActiveAgent(agent.AgentId);
        await PrintSessionHistory(agent.SessionKey);
        _console.PrintAgentIntroduction(_appConfig);
    }

    /// <summary>Fetches and displays recent session history.</summary>
    public async Task PrintSessionHistory(string sessionKey)
    {
        var history = await _gatewayService.FetchSessionHistoryAsync(sessionKey, limit: _appConfig.HistoryDisplayCount);
        if (history == null || history.Count == 0)
            return;


        // Suppress TTS during history replay
        _pttStateMachine.DuringReplay = true;
        try
        {
            _host.AddMessage("  [grey]── previous messages ──[/]");
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

    /// <summary>
    /// Forwards an OpenClaw slash command (e.g. /new, /stop) to the gateway.
    /// /new and /reset are sent via the sessions.reset RPC rather than
    /// as text commands, because the OpenClaw gateway no longer processes
    /// slash commands from chat.send (regression in OpenClaw 2026.5.x).
    /// </summary>
    public async Task HandleOpenClawCommand(string commandName, string[] args, Dictionary<string, string> named)
    {
        // Intercept /new and /reset — use sessions.reset RPC directly
        if (string.Equals(commandName, "reset", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(commandName, "new", StringComparison.OrdinalIgnoreCase))
        {
            var sessionKey = AgentRegistry.ActiveSessionKey;
            if (sessionKey == null)
            {
                _host.AddMessage("[yellow]  No active session to reset.[/]");
                return;
            }

            var reason = string.Equals(commandName, "new", StringComparison.OrdinalIgnoreCase) ? "new" : "reset";
            var displayCommand = "/" + commandName;

            try
            {
                await _gatewayService.SendRpcAsync("sessions.reset", new Dictionary<string, object?>
                {
                    ["key"] = sessionKey,
                    ["reason"] = reason
                }, CancellationToken.None);
                _console.PrintMarkupedUserMessage($"[blue on gray15]⚡ {Markup.Escape(displayCommand)} [/]");
            }
            catch (Exception ex)
            {
                _host.AddMessage($"[red]  Failed to reset session: {Markup.Escape(ex.Message)}[/]");
            }
            return;
        }

        // For all other commands, forward as text via chat.send
        var parts = new List<string> { "/" + commandName };
        parts.AddRange(args);
        foreach (var kvp in named)
            parts.Add($"{kvp.Key}={kvp.Value}");

        var commandText = string.Join(" ", parts);

        try
        {
            await _textSender.SendAsync(commandText, CancellationToken.None, printMessage: false);
            _console.PrintMarkupedUserMessage($"[blue on gray15]⚡ {Markup.Escape(commandText)} [/]");
        }
        catch (Exception ex)
        {
            _host.AddMessage($"[red]  Failed to send command: {Markup.Escape(ex.Message)}[/]");
        }
    }

    /// <summary>Handler for /errors — display recent error log entries.</summary>
    public Task HandleErrorsCommand(string[] args)
    {
        int count = 10;
        if (args.Length > 0 && int.TryParse(args[0], out var requested))
            count = Math.Clamp(requested, 1, 100);

        var entries = _errorLog.GetRecent(count);

        if (entries.Count == 0)
        {
            _host.AddMessage("[green]  No errors logged.[/]");
            return Task.CompletedTask;
        }

        _host.AddMessage($"[cyan2]  Recent errors ({entries.Count}):[/]");
        foreach (var entry in entries)
        {
            var ts = entry.Timestamp.ToString("HH:mm:ss");
            _host.AddMessage($"  [grey]{ts}[/] [bold]{Markup.Escape(entry.Code)}[/] {Markup.Escape(entry.Message)}");
            if (entry.SuggestedActions.Length > 0)
            {
                foreach (var action in entry.SuggestedActions)
                    _host.AddMessage($"    → [grey]{Markup.Escape(action)}[/]");
            }
        }
        _host.AddMessage("[grey]  Use /errors N to show more, /errors clear to clear[/]");
        return Task.CompletedTask;
    }

    /// <summary>Handler for /reconnect — attempt to reconnect to the gateway.</summary>
    public async Task HandleReconnectCommand(string[] args)
    {
        _host.AddMessage("[cyan2]  Attempting to reconnect to gateway...[/]");
        try
        {
            // GatewayService.RecreateWithConfig creates a fresh internal client
            // but we need to dispose the old one. GatewayService handles this internally.
            await _gatewayService.ConnectAsync(CancellationToken.None);
            _host.AddMessage("[green]  Reconnected successfully.[/]");
            // Pull session history for the now-active agent
            var sessionKey = AgentRegistry.ActiveSessionKey;
            if (sessionKey != null)
                await PrintSessionHistory(sessionKey);
        }
        catch (Exception ex)
        {
            var classification = GatewayErrorClassifier.Classify(ex);
            _errorLog.Write(new ErrorLogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = "error",
                Category = "gateway",
                Code = classification.Code,
                Message = classification.HumanMessage,
                SuggestedActions = classification.SuggestedActions,
                RawException = classification.RawMessage,
                StackTrace = classification.StackTrace
            });
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

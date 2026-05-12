using System;
using System.Collections.Generic;
using System.Linq;
using OpenClawPTT.ConfigWizard;
using OpenClawPTT.Services;
using Spectre.Console;
using StreamShell;

namespace OpenClawPTT;

/// <summary>
/// First-connection wizard that prompts the user to configure each agent
/// (hotkey, emoji, color) when no agents.json exists on initial gateway connection.
/// Uses the same event-driven pattern as ConfigurationWizard.
/// </summary>
public sealed class FirstConnectionWizard
{
    public static bool IsActive { get; private set; }

    private readonly IStreamShellHost _host;
    private readonly IAgentSettingsPersistence _persistence;
    private readonly Action<AgentInfo>? _onAgentConfigured;
    private Queue<AgentInfo> _pendingAgents = new();
    private AgentInfo? _currentAgent;

    public FirstConnectionWizard(IStreamShellHost host, IAgentSettingsPersistence persistence, Action<AgentInfo>? onAgentConfigured = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _onAgentConfigured = onAgentConfigured;
    }

    public void Run()
    {
        var agents = AgentRegistry.Agents;
        if (agents.Count == 0)
        {
            _host.AddMessage("[yellow]  No agents available to configure.[/]");
            return;
        }

        WizardState.Enter();
        AgentRegistry.Deactivate();
        _pendingAgents = new Queue<AgentInfo>(agents);

        _host.AddMessage("");
        _host.AddMessage("[yellow]  ╔══════════════════════════════════════════╗[/]");
        _host.AddMessage("[yellow]  ║       First connection detected!         ║[/]");
        _host.AddMessage("[yellow]  ║  No agent settings found.                ║[/]");
        _host.AddMessage("[yellow]  ║  Configure hotkeys, emojis, and colors?  ║[/]");
        _host.AddMessage("[yellow]  ╚══════════════════════════════════════════╝[/]");
        _host.AddMessage("[cyan2]  Configure agents now? (y/N)[/]");

        _host.UserInputSubmitted += OnOptInInput;
    }

    private void OnOptInInput(StreamShell.UserInputSubmittedEventArgs e)
    {
        if (e.InputType == StreamShell.InputType.Command) return;
        string? input = e.TextWithoutAttachments ?? e.RawOutput;
        if (input == null) return;

        _host.UserInputSubmitted -= OnOptInInput;

        var trimmed = input.Trim().ToLowerInvariant();

        if (trimmed == "y" || trimmed == "yes")
        {
            ProcessNextAgent();
        }
        else
        {
            WizardState.Leave();
            _host.AddMessage("[grey]  Skipped agent configuration. Use /crew config anytime.[/]");
            ActivateDefault();
        }
    }

    private void ActivateDefault()
    {
        var defaultAgent = AgentRegistry.GetDefaultAgent();
        if (defaultAgent != null)
        {
            AgentRegistry.SetActiveAgent(defaultAgent.AgentId);
            _host.AddMessage($"[cyan]  Active agent: {Markup.Escape(defaultAgent.Name)} — use /chat <name> or hotkey to switch, /crew config to edit[/]");
            _onAgentConfigured?.Invoke(defaultAgent);
        }
    }

    private void ProcessNextAgent()
    {
        if (_pendingAgents.Count == 0)
        {
            WizardState.Leave();
            _host.AddMessage("[green]  ✓ All configured![/]");
            ActivateDefault();
            return;
        }

        _currentAgent = _pendingAgents.Dequeue();
        var emoji = _persistence.GetPersistedEmoji(_currentAgent.AgentId) ?? "🤖";
        var color = _persistence.GetPersistedColor(_currentAgent.AgentId);
        var nameStr = color != null
            ? $"[{color}]{Markup.Escape(_currentAgent.Name)}[/]"
            : Markup.Escape(_currentAgent.Name);

        _host.AddMessage("");
        _host.AddMessage($"[cyan2]  Agent {emoji} {nameStr} [grey]({Markup.Escape(_currentAgent.AgentId)})[/][/]");
        _host.AddMessage($"  Press Enter to configure this agent, or type [bold]skip[/] to skip");

        _host.UserInputSubmitted += OnAgentPromptInput;
    }

    private void OnAgentPromptInput(StreamShell.UserInputSubmittedEventArgs e)
    {
        if (e.InputType == StreamShell.InputType.Command) return;
        string? input = e.TextWithoutAttachments ?? e.RawOutput;
        if (input == null) return;

        _host.UserInputSubmitted -= OnAgentPromptInput;

        var trimmed = input.Trim().ToLowerInvariant();

        if (trimmed == "skip")
        {
            _host.AddMessage($"[grey]  Skipped {Markup.Escape(_currentAgent?.Name ?? "agent")}[/]");
            ProcessNextAgent();
            return;
        }

        // Configure this agent — launch AgentConfigWizard
        if (_currentAgent != null)
        {
            var wizard = new AgentConfigWizard(_host, _persistence);
            wizard.Completed += OnAgentConfigCompleted;
            _ = wizard.RunAsync(_currentAgent);
        }
    }

    private void OnAgentConfigCompleted()
    {
        // AgentConfigWizard finished — resume the loop
        ProcessNextAgent();
    }
}

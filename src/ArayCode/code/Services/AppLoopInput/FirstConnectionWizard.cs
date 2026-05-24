using System;
using System.Collections.Generic;
using System.Linq;
using ArayCode.ConfigWizard;
using ArayCode.Services;
using ArayCode.Services.Themes;
using Spectre.Console;
using StreamShell;

namespace ArayCode;

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
    private readonly Action? _onCompleted;
    private Queue<AgentInfo> _pendingAgents = new();
    private AgentInfo? _currentAgent;

    public FirstConnectionWizard(IStreamShellHost host, IAgentSettingsPersistence persistence,
        Action<AgentInfo>? onAgentConfigured = null, Action? onCompleted = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _onAgentConfigured = onAgentConfigured;
        _onCompleted = onCompleted;
    }

    public void Run()
    {
        var agents = AgentRegistry.Agents;
        if (agents.Count == 0)
        {
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  No agents available to configure.[/]");
            return;
        }

        WizardState.Enter();
        AgentRegistry.Deactivate();
        _pendingAgents = new Queue<AgentInfo>(agents);
        _host.SetDefaultPanel(new Services.EmptyBottomPanel());

        _host.AddMessage("");
        _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  ╔══════════════════════════════════════════╗[/]");
        _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  ║       First connection detected!         ║[/]");
        _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  ║  No agent settings found.                ║[/]");
        _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  ║  Configure hotkeys, emojis, and colors?  ║[/]");
        _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  ╚══════════════════════════════════════════╝[/]");
        _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Highlight}]  Configure agents now? (y/N)[/]");

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
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Info}]  Skipped agent configuration. Use /crew config anytime.[/]");
            ActivateDefault();
        }
    }

    private void ActivateDefault()
    {
        _host.ResetDefaultPanel();
        var defaultAgent = AgentRegistry.GetDefaultAgent();
        if (defaultAgent != null)
        {
            AgentRegistry.SetActiveAgent(defaultAgent.AgentId);
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Highlight}]  Active agent: {Markup.Escape(defaultAgent.Name)} — use /chat <name> or hotkey to switch, /crew config to edit[/]");
            _onAgentConfigured?.Invoke(defaultAgent);
        }
        _onCompleted?.Invoke();
    }

    private void ProcessNextAgent()
    {
        if (_pendingAgents.Count == 0)
        {
            WizardState.Leave();
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Success}]  ✓ All configured![/]");
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
        _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Highlight}]  Agent {emoji} {nameStr} [{ThemeProvider.Current.Tools.General.Muted}]({Markup.Escape(_currentAgent.AgentId)})[/][/]");
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
            _host.AddMessage($"[{ThemeProvider.Current.Tools.General.Muted}]  Skipped {Markup.Escape(_currentAgent?.Name ?? "agent")}[/]");
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

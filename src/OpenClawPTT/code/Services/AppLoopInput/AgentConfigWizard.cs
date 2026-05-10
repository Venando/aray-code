using System;
using System.Linq;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using Spectre.Console;
using StreamShell;

namespace OpenClawPTT;

/// <summary>
/// Event-driven wizard for configuring per-agent settings (hotkey, emoji, color).
/// Follows the same pattern as <see cref="ConfigurationWizard"/>:
/// subscribes to <see cref="IStreamShellHost.UserInputSubmitted"/>,
/// sends prompts, validates input, and advances through steps.
/// </summary>
public sealed class AgentConfigWizard
{
    /// <summary>Set to true while any wizard is active, so main input handler skips processing.</summary>
    public static bool IsActive { get; private set; }

    /// <summary>Fires when the wizard completes all steps.</summary>
    public event Action? Completed;

    /// <summary>Fires after completion with the configured agent (if any), for reactivation+trace.</summary>
    public Action<AgentInfo>? OnConfigured { get; set; }

    private enum Step
    {
        SelectAgent,
        Hotkey,
        Emoji,
        Color,
        ShowInStatusPanel,
        Done
    }

    private readonly IStreamShellHost _host;
    private readonly IAgentSettingsPersistence _persistence;
    private AgentInfo? _agent;
    private Step _currentStep;
    private bool _isFirstPrompt;

    public AgentConfigWizard(IStreamShellHost host, IAgentSettingsPersistence persistence)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
    }

    public Task RunAsync(AgentInfo? preSelectedAgent = null)
    {
        IsActive = true;
        _agent = preSelectedAgent;
        _currentStep = preSelectedAgent != null ? Step.Hotkey : Step.SelectAgent;
        _isFirstPrompt = true;

        _host.UserInputSubmitted += OnUserInputSubmitted;
        SendPrompt(_currentStep);

        return Task.CompletedTask;
    }

    private void OnUserInputSubmitted(StreamShell.UserInputSubmittedEventArgs e)
    {
        try
        {
            if (e.InputType == StreamShell.InputType.Command) return;
            string? input = e.TextWithoutAttachments ?? e.RawOutput;
            if (input == null) return;

            var rawInput = input.Trim();
            var step = _currentStep;

            // Handle SelectAgent step
            if (step == Step.SelectAgent)
            {
                var matched = AgentRegistry.Agents.FirstOrDefault(a =>
                    a.Name.Equals(rawInput, StringComparison.OrdinalIgnoreCase) ||
                    a.AgentId.Equals(rawInput, StringComparison.OrdinalIgnoreCase));
                if (matched == null)
                {
                    _host.AddMessage($"[red]  ✗ Agent not found: {Markup.Escape(rawInput)}[/]");
                    SendPrompt(step);
                    return;
                }
                _agent = matched;
                _host.AddMessage($"[green]  ✓ {Markup.Escape(matched.Name)}[/]");
                Advance();
                return;
            }

            if (_agent == null)
                return;

            // "--" means clear the field (set to null)
            if (rawInput == "--")
            {
                ApplyValue(step, null);
                _host.AddMessage("[green]  ✓ (cleared)[/]");
                Advance();
                return;
            }

            // Empty input keeps the current value — just advance
            if (string.IsNullOrEmpty(rawInput))
            {
                _host.AddMessage("[green]  ✓ (kept current)[/]");
                Advance();
                return;
            }

            // Validate
            if (!Validate(step, rawInput, out var errorHint))
            {
                _host.AddMessage($"[red]  ✗ {errorHint}[/]");
                SendPrompt(step);
                return;
            }

            ApplyValue(step, rawInput);
            _host.AddMessage($"[green]  ✓ {Markup.Escape(rawInput)}[/]");
            Advance();
        }
        catch (Exception ex)
        {
            _host.AddMessage($"[red]  ✗ Wizard error: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private void Advance()
    {
        _currentStep++;

        if (_currentStep == Step.Done)
        {
            Unsubscribe();
            _host.AddMessage($"[green]  ✓ Configuration complete for {Markup.Escape(_agent?.Name ?? "agent")}![/]");
            return;
        }

        SendPrompt(_currentStep);
    }

    private void Unsubscribe()
    {
        IsActive = false;
        _host.UserInputSubmitted -= OnUserInputSubmitted;
        Completed?.Invoke();
        if (_agent != null)
        {
            // Force a save so the file is created even if all defaults were kept
            _persistence.SetPersistedHotkey(_agent.AgentId, _persistence.GetPersistedHotkey(_agent.AgentId));
            OnConfigured?.Invoke(_agent);
        }
    }

    private void SendPrompt(Step step)
    {
        if (!_isFirstPrompt)
        {
            _host.AddMessage(""); // blank line between prompts
        }
        _isFirstPrompt = false;

        var (description, currentValue) = GetStepInfo(step);
        _host.AddMessage($"[cyan2]▸ {description}[/]");
        if (currentValue != null)
        {
            var escaped = Markup.Escape(currentValue);
            _host.AddMessage($"  [grey](current: {escaped}, press Enter to keep)[/]");
        }
        else
        {
            _host.AddMessage($"  [grey](current: none)[/]");
        }
    }

    private (string Description, string? CurrentValue) GetStepInfo(Step step)
    {
        switch (step)
        {
            case Step.SelectAgent:
                return ("Type the agent name or ID to configure", null);
            case Step.Hotkey:
                var agentHotkey = _agent != null ? _persistence.GetPersistedHotkey(_agent.AgentId) : null;
                return ("Set hotkey combination (e.g. Alt+1, Ctrl+F5, -- to clear)", agentHotkey);
            case Step.Emoji:
                var agentEmoji = _agent != null ? _persistence.GetPersistedEmoji(_agent.AgentId) : null;
                return ("Set emoji (e.g. 🎮, 🐧, 🤖 — -- to clear)", agentEmoji);
            case Step.Color:
                var agentColor = _agent != null ? _persistence.GetPersistedColor(_agent.AgentId) : null;
                return ("Set color (Spectre.Console color name or #hex, e.g. cyan2, springgreen4, #ff6600 — -- to clear).\n       See https://spectreconsole.net/console/reference/color-reference for color names", agentColor);
            case Step.ShowInStatusPanel:
                var show = _agent != null ? _persistence.GetPersistedShowInStatusPanel(_agent.AgentId) : true;
                return ("Show in status panel? (yes/no — -- to clear/reset to yes)", show ? "yes" : "no");
            default:
                return (step.ToString(), null);
        }
    }

    private static bool Validate(Step step, string rawInput, out string errorHint)
    {
        errorHint = "";

        switch (step)
        {
            case Step.Hotkey:
                try
                {
                    HotkeyMapping.Parse(rawInput);
                    return true;
                }
                catch (Exception ex)
                {
                    errorHint = $"Invalid hotkey: {Markup.Escape(ex.Message)}";
                    return false;
                }
            case Step.Emoji:
                // Accept any string as emoji
                return true;
            case Step.Color:
                // Accept any string — Spectre will validate at render time
                return true;
            case Step.ShowInStatusPanel:
                var ok = rawInput.Equals("yes", StringComparison.OrdinalIgnoreCase)
                      || rawInput.Equals("no", StringComparison.OrdinalIgnoreCase)
                      || rawInput.Equals("y", StringComparison.OrdinalIgnoreCase)
                      || rawInput.Equals("n", StringComparison.OrdinalIgnoreCase);
                if (!ok) errorHint = "Enter yes, no, y, or n";
                return ok;
            default:
                return true;
        }
    }

    private void ApplyValue(Step step, string? value)
    {
        if (_agent == null)
            return;

        switch (step)
        {
            case Step.Hotkey:
                _persistence.SetPersistedHotkey(_agent.AgentId, value);
                break;
            case Step.Emoji:
                _persistence.SetPersistedEmoji(_agent.AgentId, value);
                break;
            case Step.Color:
                _persistence.SetPersistedColor(_agent.AgentId, value);
                break;
            case Step.ShowInStatusPanel:
                var show = value == null || value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value.Equals("y", StringComparison.OrdinalIgnoreCase);
                _persistence.SetPersistedShowInStatusPanel(_agent.AgentId, show);
                break;
        }
    }
}

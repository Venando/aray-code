using Spectre.Console;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using ArayCode.Services;
using ArayCode.ConfigWizard;

namespace ArayCode;

/// <summary>
/// Listens to ALL agent hotkeys and routes them:
/// - Active agent's hotkey → recording via PttController
/// - Inactive agent's hotkey → switch agent and show message
/// Falls back to direct PttController trigger when no agents are configured.
/// </summary>
public sealed class AgentHotkeyService : IDisposable
{
    private readonly IPttController _pttController;
    private readonly ITextMessageSender _textSender;
    private readonly IStreamShellHost _shellHost;
    private readonly AppConfig _cfg;
    private readonly IGatewayService? _gatewayService;
    private readonly IGlobalHotkeyHook? _hook;
    private readonly IColorConsole _console;
    private readonly IAgentSettingsPersistence _agentSettingsPersistence;
    private readonly IPttStateMachine? _pttStateMachine;

    // Maps agent session key → saved input field ID for preserving text across switches.
    // ConcurrentDictionary because HotkeyPressed events arrive on thread-pool threads
    // (QueueUserWorkItem in StreamShellHotkeyHook/WindowsHotkeyHook) and can overlap.
    private readonly ConcurrentDictionary<string, string> _savedInputs = new();

    public AgentHotkeyService(
        IPttController pttController,
        ITextMessageSender textSender,
        IStreamShellHost shellHost,
        AppConfig cfg,
        IAgentSettingsPersistence agentSettingsPersistence,
        IGatewayService? gatewayService = null,
        IPttStateMachine? pttStateMachine = null,
        IHotkeyHookFactory? hookFactory = null,
        IColorConsole? console = null)
    {
        _pttController = pttController;
        _textSender = textSender;
        _shellHost = shellHost;
        _cfg = cfg;
        _agentSettingsPersistence = agentSettingsPersistence;
        _gatewayService = gatewayService;
        _pttStateMachine = pttStateMachine;
        _console = console ?? new ColorConsole(shellHost);

        // Always create a hook — at minimum for Escape key cancellation.
        if (hookFactory != null)
        {
            _hook = hookFactory.Create(new Hotkey(new Key(' '), new HashSet<Modifier>()), _console);
        }
        else
        {
            _hook = GlobalHotkeyHookFactory.Create(_console, _cfg, _shellHost);
        }

        if (AgentRegistry.Agents.Count > 0)
        {
            RegisterAllAgentHotkeys();
            _hook.HotkeyIndexPressed += OnHotkeyPressed;
            _hook.HotkeyIndexReleased += OnHotkeyReleased;
        }
        else
        {
            // No agents yet — register the global config hotkey as a fallback
            // so recording still works with the PTT hotkey.
            var defaultHotkey = HotkeyMapping.Parse(_cfg.HotkeyCombination);
            _hook.SetHotkey(defaultHotkey);
            _hook.HotkeyPressed += OnDefaultHotkeyPressed;
            _hook.HotkeyReleased += OnDefaultHotkeyReleased;
        }

        _hook.EscapePressed += OnEscapePressed;
        _hook.Start();

        _agentSettingsPersistence.PersistedSettingsChanged += OnPersistedSettingsChanged;
    }

    private void RegisterAllAgentHotkeys()
    {
        if (_hook == null) return;
        var hotkeys = _agentSettingsPersistence.AllAgentsWithHotkeys
            .Select(a => HotkeyMapping.Parse(a.Hotkey ?? _cfg.HotkeyCombination))
            .ToList();
        _hook.SetHotkeys(hotkeys);
    }

    /// <summary>Called when a hotkey fires for the agent at the given index.</summary>
    public void HandleHotkeyPressed(int agentIndex)
    {
        if (WizardState.IsActive)
            return;
            
        var agents = AgentRegistry.Agents;
        if (agentIndex < 0 || agentIndex >= agents.Count)
            return;

        var agent = agents[agentIndex];
        var activeKey = AgentRegistry.ActiveSessionKey;

        if (agent.SessionKey == activeKey)
        {
            // Block Escape from reaching StreamShell while recording
            if (_hook != null) _hook.BlockEscape = true;
            _pttController.StartRecording();
        }
        else
        {
            // If a recording was in progress for the previous agent, cancel it
            // so the transcribed audio doesn't go to the newly switched agent.
            _pttController.CancelRecording();
            if (_hook != null) _hook.BlockEscape = false;

            // Save current input for the previously active agent before switching
            var inputHandler = _shellHost.InputHandler;
            if (activeKey != null && inputHandler != null)
            {
                string savedId = inputHandler.SaveInputField();
                _savedInputs[activeKey] = savedId;
            }

            // Clear input field so old text doesn't show during history playback
            if (inputHandler?.CurrentInput.Length > 0)
                inputHandler?.SetInputFieldContent("");

            // Switch active agent
            AgentRegistry.SetActiveAgent(agent.AgentId);

            // Restore saved input for the target agent (if previously saved)
            if (inputHandler != null && _savedInputs.TryRemove(agent.SessionKey, out var restoredId))
            {
                inputHandler.LoadInputField(restoredId);
            }

            // Fetch and print session history, then agent intro (fire-and-forget)
            if (_gatewayService != null && PrintSessionHistoryAsync != null)
            {
                _ = PrintSessionHistorySafe(agent.SessionKey);
            }
        }
    }

    /// <summary>Called when a hotkey is released for the agent at the given index.</summary>
    public void HandleHotkeyReleased(int agentIndex)
    {
        if (!_cfg.HoldToTalk) return;

        var agents = AgentRegistry.Agents;
        if (agentIndex < 0 || agentIndex >= agents.Count)
            return;

        var agent = agents[agentIndex];
        var activeKey = AgentRegistry.ActiveSessionKey;

        if (agent.SessionKey == activeKey)
        {
            _pttController.StopRecording();
        }
    }

    /// <summary>
    /// Optional delegate for printing session history after an agent switch.
    /// When set, used instead of the local duplicate logic.
    /// Wired by AppRunner to point at StreamShellInputHandler.PrintSessionHistory.
    /// </summary>
    public Func<string, Task>? PrintSessionHistoryAsync { get; set; }

    /// <summary>Fire-and-forget wrapper with exception guard for the session history call.</summary>
    private async Task PrintSessionHistorySafe(string sessionKey)
    {
        try
        {
            if (PrintSessionHistoryAsync != null)
                await PrintSessionHistoryAsync(sessionKey);
        }
        catch (Exception ex)
        {
            _console.LogError("hotkey", $"Session history failed after agent switch: {ex.Message}");
        }
    }

    private void OnHotkeyPressed(int index) => HandleHotkeyPressed(index);
    private void OnHotkeyReleased(int index) => HandleHotkeyReleased(index);

    private void OnDefaultHotkeyPressed()
    {
        if (_hook != null) _hook.BlockEscape = true;
        _pttController.StartRecording();
    }

    private void OnDefaultHotkeyReleased()
    {
        if (_cfg.HoldToTalk)
            _pttController.StopRecording();
    }

    private void OnPersistedSettingsChanged()
    {
        if (_hook == null) return;

        // Unsubscribe fallback events (if any) before re-registering with indexed events.
        _hook.HotkeyPressed -= OnDefaultHotkeyPressed;
        _hook.HotkeyReleased -= OnDefaultHotkeyReleased;
        _hook.HotkeyIndexPressed -= OnHotkeyPressed;
        _hook.HotkeyIndexReleased -= OnHotkeyReleased;

        if (AgentRegistry.Agents.Count > 0)
        {
            RegisterAllAgentHotkeys();
            _hook.HotkeyIndexPressed += OnHotkeyPressed;
            _hook.HotkeyIndexReleased += OnHotkeyReleased;
        }
        else
        {
            // Still no agents — keep using the global default hotkey.
            var defaultHotkey = HotkeyMapping.Parse(_cfg.HotkeyCombination);
            _hook.SetHotkey(defaultHotkey);
            _hook.HotkeyPressed += OnDefaultHotkeyPressed;
            _hook.HotkeyReleased += OnDefaultHotkeyReleased;
        }
    }

    private void OnEscapePressed()
    {
        // Unblock Escape so the next press reaches StreamShell for input clearing
        if (_hook != null) _hook.BlockEscape = false;
        _pttController.CancelRecording();
    }

    public void Dispose()
    {
        _agentSettingsPersistence.PersistedSettingsChanged -= OnPersistedSettingsChanged;

        // Clean up any saved input field states
        _savedInputs.Clear();
        _shellHost.InputHandler?.RemoveAllSavedInputFields();

        if (_hook != null)
        {
            _hook.HotkeyIndexPressed -= OnHotkeyPressed;
            _hook.HotkeyIndexReleased -= OnHotkeyReleased;
            _hook.HotkeyPressed -= OnDefaultHotkeyPressed;
            _hook.HotkeyReleased -= OnDefaultHotkeyReleased;
            _hook.EscapePressed -= OnEscapePressed;
            _hook.Dispose();
        }
    }
}

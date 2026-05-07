using Spectre.Console;
using System;
using System.Linq;
using System.Threading;
using OpenClawPTT.Services;

namespace OpenClawPTT;

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
            _hook = GlobalHotkeyHookFactory.Create(_console);
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
            AgentRegistry.SetActiveAgent(agent.AgentId);
            // Fetch and print session history, then agent intro (fire-and-forget)
            if (_gatewayService != null)
                _ = PrintHistoryAfterSwitchAsync(agent.SessionKey);
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

    private async Task PrintHistoryAfterSwitchAsync(string sessionKey)
    {
        var history = await _gatewayService!.FetchSessionHistoryAsync(sessionKey, limit: 5);
        if (history != null && history.Count > 0)
        {
            // Suppress TTS during history replay
            if (_pttStateMachine != null) _pttStateMachine.DuringReplay = true;
            try
            {
                _shellHost.AddMessage("  [grey]── previous messages ──[/]");
                foreach (var entry in history)
                {
                    if (entry.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                        _console.PrintUserMessage(entry.Content);
                    else
                        _gatewayService!.DisplayHistoryEntry(entry);
                }
            }
            finally
            {
                if (_pttStateMachine != null) _pttStateMachine.DuringReplay = false;
            }
        }

        // Print agent intro after history (so it appears at the bottom)
        _console.PrintAgentIntroduction(_cfg);
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

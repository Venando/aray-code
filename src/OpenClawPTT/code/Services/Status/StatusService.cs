using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenClawPTT.Services.StatusParts;

namespace OpenClawPTT.Services;

/// <summary>
/// Tracks gateway, TTS, direct LLM, and agent status, rendering discrete
/// status info parts (active agent name, model, thinking level, context
/// usage, conversation name, connection status, direct LLM status, main
/// agents list) on the StreamShell separator bars.
///
/// Each part is a separate <see cref="IStatusPart"/> implementation with
/// its own dirty-flag tracking and text caching.  Parts are collected into
/// position groups (TopSeparatorLeft, TopSeparatorRight, AppStatusPanelLeft,
/// AppStatusPanelRight, etc.) based on <see cref="AppConfig"/> settings,
/// then rendered in order.
///
/// Thread-safe: all public methods synchronize on a lock before mutating
/// state.  Subscribes to the tracker's <see cref="IAgentStatusTracker.Changed"/>
/// event and triggers a re-render whenever a snapshot updates.
/// Rendering mechanics are delegated to <see cref="StatusRenderer"/>.
/// </summary>
public sealed class StatusService : IStatusService, IDisposable
{
    private readonly IStreamShellHost _shellHost;
    private readonly StatusRenderer _renderer;
    private readonly StatusAnimationManager _animationManager;
    private IAgentStatusTracker? _agentTracker;
    private readonly object _lock = new();

    // Status parts — each is a discrete, cacheable rendering unit
    private readonly ActiveAgentPart _activeAgentPart;
    private readonly ModelPart _modelPart;
    private readonly ThinkingLevelPart _thinkingLevelPart;
    private readonly ContextPart _contextPart;
    private readonly ConversationNamePart _conversationNamePart;
    private readonly ServiceStatusPart _gatewayStatusPart;
    private readonly ServiceStatusPart _ttsStatusPart;
    private readonly ServiceStatusPart _sttStatusPart;
    private readonly ServiceStatusPart _llmStatusPart;
    private MainAgentsPart? _mainAgentsPart;

    // All animated parts (ServiceStatusPart instances) for efficient animation ticking
    private readonly ServiceStatusPart[] _animatedParts;

    // All parts in a flat list for iteration; rebuilt when MainAgentsPart is set
    private IStatusPart[] _allParts;

    // Map ServiceKind to the corresponding ServiceStatusPart
    private readonly Dictionary<ServiceKind, ServiceStatusPart> _serviceParts;

    public StatusService(IStreamShellHost shellHost, IAgentStatusTracker? agentStatusTracker = null, MainAgentsPart? mainAgentsPart = null)
    {
        _shellHost = shellHost ?? throw new ArgumentNullException(nameof(shellHost));
        _renderer = new StatusRenderer(shellHost);
        _agentTracker = agentStatusTracker;

        // Create parts with default positions; ApplyConfigPositions() will override
        _activeAgentPart = new ActiveAgentPart();
        _modelPart = new ModelPart();
        _thinkingLevelPart = new ThinkingLevelPart();
        _contextPart = new ContextPart();
        _conversationNamePart = new ConversationNamePart();
        _gatewayStatusPart = new ServiceStatusPart("GW:", order: 1);
        _ttsStatusPart = new ServiceStatusPart("TTS:", order: 2);
        _sttStatusPart = new ServiceStatusPart("STT:", order: 3);
        _llmStatusPart = new ServiceStatusPart("LLM:", order: 4);

        // Build service-part lookup
        _serviceParts = new Dictionary<ServiceKind, ServiceStatusPart>
        {
            [ServiceKind.Gateway] = _gatewayStatusPart,
            [ServiceKind.Tts] = _ttsStatusPart,
            [ServiceKind.Stt] = _sttStatusPart,
            [ServiceKind.DirectLlm] = _llmStatusPart,
        };

        // Collect all animated parts for periodic frame advancement
        _animatedParts = [_gatewayStatusPart, _ttsStatusPart, _sttStatusPart, _llmStatusPart];

        if (mainAgentsPart != null)
            _mainAgentsPart = mainAgentsPart;

        _allParts = BuildAllParts();

        if (_agentTracker != null)
            _agentTracker.Changed += OnAgentStatusChanged;

        // React to agent switching in the registry (e.g. /crew or hotkey switch)
        AgentRegistry.ActiveSessionChanged += OnActiveSessionChanged;

        // Start animation manager for yellow-status dots
        _animationManager = new StatusAnimationManager(_animatedParts, OnAnimationTick);
    }

    public void SetMainAgentsPart(MainAgentsPart part)
    {
        Mutate(() =>
        {
            _mainAgentsPart = part ?? throw new ArgumentNullException(nameof(part));
            _allParts = BuildAllParts();
        });
    }

    private IStatusPart[] BuildAllParts()
    {
        var baseParts = new List<IStatusPart>(10)
        {
            _activeAgentPart,
            _modelPart,
            _thinkingLevelPart,
            _contextPart,
            _conversationNamePart,
            _gatewayStatusPart,
            _ttsStatusPart,
            _sttStatusPart,
            _llmStatusPart,
        };

        if (_mainAgentsPart != null)
            baseParts.Add(_mainAgentsPart);

        return baseParts.ToArray();
    }

    /// <summary>
    /// The <see cref="MainAgentsPart"/> owned by this service.
    /// Exposed for <see cref="AppStatusBottomPanel"/> to use as its data source.
    /// </summary>
    public MainAgentsPart? MainAgentsPart => _mainAgentsPart;

    /// <inheritdoc />
    public void SetServiceStatus(ServiceKind kind, StatusColor color)
    {
        if (_serviceParts.TryGetValue(kind, out var part))
            Mutate(() => part.SetStatus(color));
    }

    /// <inheritdoc />
    public StatusColor? GetServiceStatus(ServiceKind kind)
    {
        lock (_lock)
        {
            return _serviceParts.TryGetValue(kind, out var part) ? part.GetCurrentColor() : null;
        }
    }

    public void SetAgentStatusTracker(IAgentStatusTracker tracker)
    {
        Mutate(() =>
        {
            // Unsubscribe from previous tracker if any
            if (_agentTracker != null)
                _agentTracker.Changed -= OnAgentStatusChanged;

            _agentTracker = tracker;
            _agentTracker.Changed += OnAgentStatusChanged;
        });
    }

    public void SetConversationName(string? name)
    {
        Mutate(() => _conversationNamePart.Update(name));
    }

    /// <inheritdoc />
    public void ApplyConfigPositions(AppConfig cfg)
    {
        Mutate(() =>
        {
            _activeAgentPart.Position = cfg.ActiveAgentPosition;
            _modelPart.Position = cfg.ModelPosition;
            _thinkingLevelPart.Position = cfg.ThinkingLevelPosition;
            _contextPart.Position = cfg.ContextPosition;
            _conversationNamePart.Position = cfg.ConversationNamePosition;
            _gatewayStatusPart.Position = cfg.ConnectionStatusPosition;
            _ttsStatusPart.Position = cfg.TtsStatusPosition;
            _sttStatusPart.Position = cfg.SttStatusPosition;
            _llmStatusPart.Position = cfg.DirectLlmPosition;
            if (_mainAgentsPart != null)
                _mainAgentsPart.Position = cfg.MainAgentsPosition;
        });
    }

    // ── Lifecycle & event handling ──────────────────────────────────────

    /// <summary>
    /// Acquires the lock, executes the action, forces a re-render, and
    /// manages the animation timer.  Nearly every public method on
    /// StatusService follows this pattern — extracted here for DRY.
    /// </summary>
    private void Mutate(Action action)
    {
        lock (_lock)
        {
            action();
            Render();
        }
    }

    /// <summary>
    /// Called when the active agent session changes in the registry.
    /// Triggers a re-render so agent-based parts reflect the switched agent.
    /// </summary>
    private void OnActiveSessionChanged(string? _)
    {
        lock (_lock)
        {
            _activeAgentPart.OnActiveSessionChanged();
            RefreshAgentData();
            Render();
        }
    }

    /// <summary>
    /// Called when the agent status tracker fires its Changed event.
    /// Re-renders the separator bars with updated agent info.
    /// </summary>
    private void OnAgentStatusChanged()
    {
        lock (_lock)
        {
            RefreshAgentData();
            Render();
        }
    }

    /// <summary>
    /// Called on a timer thread when an animation tick fires.  Acquires the
    /// lock, advances frames, and re-renders if any part became dirty.
    /// </summary>
    private void OnAnimationTick()
    {
        lock (_lock)
        {
            _animationManager.AdvanceFrames();

            bool anyAnimating = false;
            foreach (var part in _animatedParts)
            {
                if (part.IsDirty)
                {
                    anyAnimating = true;
                    break;
                }
            }

            if (anyAnimating)
                Render();
        }
    }

    // ── Data refresh ────────────────────────────────────────────────────

    /// <summary>
    /// Reads the active agent snapshot from the tracker and feeds the
    /// data values into each agent-dependent part.  Parts internally
    /// detect whether values actually changed before marking dirty.
    /// </summary>
    private void RefreshAgentData()
    {
        var snapshot = GetActiveSnapshot();

        if (snapshot is null)
        {
            _activeAgentPart.Update(null);
            _modelPart.Update(null);
            _thinkingLevelPart.Update(null);
            _contextPart.Update(null, null);
            return;
        }

        _activeAgentPart.Update(snapshot);
        _modelPart.Update(snapshot.Model);
        _thinkingLevelPart.Update(snapshot.ThinkingDefault);
        _contextPart.Update(snapshot.ContextTokens,
            snapshot.TotalTokens ?? snapshot.InputTokens);
    }

    /// <summary>Gets the snapshot for the currently active agent, if any.</summary>
    private AgentStatusSnapshot? GetActiveSnapshot()
    {
        if (_agentTracker == null)
            return null;

        var activeSessionKey = AgentRegistry.ActiveSessionKey;
        if (string.IsNullOrEmpty(activeSessionKey))
            return null;

        return _agentTracker.Get(activeSessionKey);
    }

    // ── Render ──────────────────────────────────────────────────────────

    /// <summary>
    /// Delegates to <see cref="StatusRenderer"/> for composition and text push,
    /// then manages animation state.
    /// </summary>
    private void Render()
    {
        _renderer.Render(_allParts);
        StatusRenderer.MarkAllClean(_allParts);
        _animationManager.EnsureRunning();
    }

    public void Dispose()
    {
        _animationManager.Dispose();

        AgentRegistry.ActiveSessionChanged -= OnActiveSessionChanged;

        if (_agentTracker != null)
            _agentTracker.Changed -= OnAgentStatusChanged;

        _mainAgentsPart?.Dispose();
    }
}

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
/// </summary>
public sealed class StatusService : IStatusService, IDisposable
{
    private const string RepeatedCharacterMarkup = "white";
    private const string LeftSeparator = "──────────────── ";

    private readonly IStreamShellHost _shellHost;
    private IAgentStatusTracker? _agentTracker;
    private readonly object _lock = new();

    // Status parts — each is a discrete, cacheable rendering unit
    private readonly ActiveAgentPart _activeAgentPart;
    private readonly ModelPart _modelPart;
    private readonly ThinkingLevelPart _thinkingLevelPart;
    private readonly ContextPart _contextPart;
    private readonly ConversationNamePart _conversationNamePart;
    private readonly ConnectionStatusPart _connectionStatusPart;
    private readonly DirectLlmStatusPart _directLlmStatusPart;
    private MainAgentsPart? _mainAgentsPart;

    // All parts in a flat list for iteration; rebuilt when MainAgentsPart is set
    private IStatusPart[] _allParts;

    // Reusable per-position lists to avoid allocations in Render()
    private readonly List<IStatusPart> _topLeft = new(6);
    private readonly List<IStatusPart> _topRight = new(6);
    private readonly List<IStatusPart> _bottomLeft = new(6);
    private readonly List<IStatusPart> _bottomRight = new(6);

    // Reusable StringBuilders for composing final left/right strings
    private readonly StringBuilder _sbLeft = new(256);
    private readonly StringBuilder _sbRight = new(128);

    public StatusService(IStreamShellHost shellHost, IAgentStatusTracker? agentStatusTracker = null, MainAgentsPart? mainAgentsPart = null)
    {
        _shellHost = shellHost ?? throw new ArgumentNullException(nameof(shellHost));
        _agentTracker = agentStatusTracker;

        // Create parts with default positions; ApplyConfigPositions() will override
        _activeAgentPart = new ActiveAgentPart();
        _modelPart = new ModelPart();
        _thinkingLevelPart = new ThinkingLevelPart();
        _contextPart = new ContextPart();
        _conversationNamePart = new ConversationNamePart();
        _connectionStatusPart = new ConnectionStatusPart();
        _directLlmStatusPart = new DirectLlmStatusPart();

        // MainAgentsPart may be injected or set later via SetMainAgentsPart()
        if (mainAgentsPart != null)
            _mainAgentsPart = mainAgentsPart;

        _allParts = BuildAllParts();

        if (_agentTracker != null)
            _agentTracker.Changed += OnAgentStatusChanged;

        // React to agent switching in the registry (e.g. /crew or hotkey switch)
        AgentRegistry.ActiveSessionChanged += OnActiveSessionChanged;
    }

    /// <summary>
    /// Sets the <see cref="MainAgentsPart"/> to use for the agents list.
    /// Must be called before the first render if not injected via constructor.
    /// </summary>
    public void SetMainAgentsPart(MainAgentsPart part)
    {
        lock (_lock)
        {
            _mainAgentsPart = part ?? throw new ArgumentNullException(nameof(part));
            _allParts = BuildAllParts();
            Render();
        }
    }

    private IStatusPart[] BuildAllParts()
    {
        var baseParts = new List<IStatusPart>(8)
        {
            _activeAgentPart,
            _modelPart,
            _thinkingLevelPart,
            _contextPart,
            _conversationNamePart,
            _connectionStatusPart,
            _directLlmStatusPart,
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

    public void SetGatewayStatus(string label, StatusColor color)
    {
        lock (_lock)
        {
            _connectionStatusPart.SetGatewayStatus(label, color);
            Render();
        }
    }

    public void SetTtsStatus(string label, StatusColor color)
    {
        lock (_lock)
        {
            _connectionStatusPart.SetTtsStatus(label, color);
            Render();
        }
    }

    public void SetDirectLlmStatus(string label, StatusColor color)
    {
        lock (_lock)
        {
            _directLlmStatusPart.SetStatus(label, color);
            Render();
        }
    }

    public void SetDirectLlmLastCalled(DateTime? timestamp)
    {
        lock (_lock)
        {
            _directLlmStatusPart.SetLastCalled(timestamp);
            Render();
        }
    }

    public void SetAgentStatusTracker(IAgentStatusTracker tracker)
    {
        lock (_lock)
        {
            // Unsubscribe from previous tracker if any
            if (_agentTracker != null)
                _agentTracker.Changed -= OnAgentStatusChanged;

            _agentTracker = tracker;
            _agentTracker.Changed += OnAgentStatusChanged;
            Render();
        }
    }

    public void SetConversationName(string? name)
    {
        lock (_lock)
        {
            _conversationNamePart.Update(name);
            Render();
        }
    }

    /// <inheritdoc />
    public void ApplyConfigPositions(AppConfig cfg)
    {
        lock (_lock)
        {
            _activeAgentPart.Position = cfg.ActiveAgentPosition;
            _modelPart.Position = cfg.ModelPosition;
            _thinkingLevelPart.Position = cfg.ThinkingLevelPosition;
            _contextPart.Position = cfg.ContextPosition;
            _conversationNamePart.Position = cfg.ConversationNamePosition;
            _connectionStatusPart.Position = cfg.ConnectionStatusPosition;
            _directLlmStatusPart.Position = cfg.DirectLlmPosition;
            if (_mainAgentsPart != null)
                _mainAgentsPart.Position = cfg.MainAgentsPosition;
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

    /// <summary>
    /// Builds and renders the separator bars by collecting parts per
    /// position group, sorting them by <see cref="IStatusPart.Order"/>,
    /// composing the text, and pushing it to the StreamShell host.
    /// Only parts with dirty text are re-rendered; the rest use cached
    /// values.  Parts with <see cref="DisplayPosition.None"/> are skipped.
    /// </summary>
    private void Render()
    {
        try
        {
            // Collect parts into position groups
            _topLeft.Clear();
            _topRight.Clear();
            _bottomLeft.Clear();
            _bottomRight.Clear();

            foreach (var part in _allParts)
            {
                switch (part.Position)
                {
                    case DisplayPosition.TopSeparatorLeft:
                        _topLeft.Add(part);
                        break;
                    case DisplayPosition.TopSeparatorRight:
                        _topRight.Add(part);
                        break;
                    case DisplayPosition.BottomSeparatorLeft:
                    case DisplayPosition.AppStatusPanelLeft:
                        _bottomLeft.Add(part);
                        break;
                    case DisplayPosition.BottomSeparatorRight:
                    case DisplayPosition.AppStatusPanelRight:
                        _bottomRight.Add(part);
                        break;
                    // DisplayPosition.None: skip entirely
                }
            }

            // Sort each group by Order
            SortByOrder(_topLeft);
            SortByOrder(_topRight);
            SortByOrder(_bottomLeft);
            SortByOrder(_bottomRight);

            // Build and set top separator
            string topLeftText = BuildTopLeftText(ComposePositionText(_topLeft));
            string topRightText = ComposePositionText(_topRight);
            _shellHost.SetTopSeparator(leftText: topLeftText, rightText: topRightText,
                repeatedCharacter: '─', repeatedCharMarkup: RepeatedCharacterMarkup);

            // Build and set bottom separator if any parts are assigned to it
            if (_bottomLeft.Count > 0 || _bottomRight.Count > 0)
            {
                string bottomLeftText = ComposePositionText(_bottomLeft);
                string bottomRightText = ComposePositionText(_bottomRight);
                _shellHost.SetBottomSeparator(leftText: bottomLeftText, rightText: bottomRightText,
                    repeatedCharacter: '─', repeatedCharMarkup: RepeatedCharacterMarkup);
            }

            // Mark all clean — the rendered strings have been consumed by the shell host
            MarkAllClean();
        }
        catch (Exception ex)
        {
            // Rendering is best-effort — never crash the caller if shell is disposed
            System.Diagnostics.Debug.WriteLine($"StatusService.Render failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds the top separator left-side text by prepending the separator
    /// line prefix. When the left side is empty, returns empty string.
    /// </summary>
    private string BuildTopLeftText(string partsText)
    {
        if (string.IsNullOrEmpty(partsText))
            return string.Empty;

        _sbLeft.Clear();
        _sbLeft.Append(LeftSeparator);
        _sbLeft.Append(partsText);
        _sbLeft.Append(' ');
        return _sbLeft.ToString();
    }

    /// <summary>
    /// Composes the text for a list of parts by concatenating their
    /// rendered values with appropriate separators.  Uses cached text
    /// for non-dirty parts and re-renders only dirty ones.
    /// </summary>
    private string ComposePositionText(List<IStatusPart> parts)
    {
        if (parts.Count == 0)
            return string.Empty;

        _sbRight.Clear();
        bool first = true;

        foreach (var part in parts)
        {
            string text = part.GetText();
            if (string.IsNullOrEmpty(text))
                continue;

            if (!first)
            {
                _sbRight.Append(part.SeparatorBefore);
            }

            _sbRight.Append(text);
            first = false;
        }

        return _sbRight.ToString();
    }

    /// <summary>Calls <see cref="IStatusPart.MarkClean"/> on every dirty part.</summary>
    private void MarkAllClean()
    {
        foreach (var part in _allParts)
        {
            if (part.IsDirty)
                part.MarkClean();
        }
    }

    /// <summary>Sorts a list of parts in-place by <see cref="IStatusPart.Order"/>.</summary>
    private static void SortByOrder(List<IStatusPart> parts)
    {
        if (parts.Count > 1)
            parts.Sort((a, b) => a.Order.CompareTo(b.Order));
    }

    public void Dispose()
    {
        AgentRegistry.ActiveSessionChanged -= OnActiveSessionChanged;

        if (_agentTracker != null)
            _agentTracker.Changed -= OnAgentStatusChanged;

        _mainAgentsPart?.Dispose();
    }
}
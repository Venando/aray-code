using System.Text;
using OpenClawPTT.Formatting;
using OpenClawPTT.Services.Themes;
using Spectre.Console;

namespace OpenClawPTT.Services.StatusParts;

/// <summary>
/// Renders the main agents list as a single Spectre-markup line, e.g.
/// "│ 🤖Kimi ✅ │ 🤖Claude ⏳". Skips the active agent and subagents;
/// respects per-agent ShowInStatusPanel settings.
/// </summary>
public sealed class MainAgentsPart : StatusPartBase, IDisposable
{
    public event Action? Changed;

    private const int MaxAgentNameLength = 10;
    private const string NoAgentsText = "No agents connected";
    private static string NoAgentsTextMarkup
        => $"[{ThemeProvider.Current.Tools.StatusBar.NoAgentsText}]{NoAgentsText}[/]";

    private static string ReadyEmoji => $"[{ThemeProvider.Current.Tools.Messages.Success}]•[/]";
    private const string NotificationEmoji = "❗";

    private readonly IAgentActivityStore _tracker;

    private readonly List<(SessionStateEvent State, AgentInfo Agent)> _visible = new();
    private readonly Dictionary<string, AgentInfo> _agentLookup = new();

    private int _lastRegistryCount;
    private readonly HashSet<string> _newlyOnlineAgents = new();
    private readonly Dictionary<string, string> _previousStatusEmojis = new();

    private bool _disposed;

    public MainAgentsPart(
        IAgentActivityStore tracker,
        DisplayPosition defaultPosition = DisplayPosition.AppStatusPanelLeft,
        int order = 0)
        : base(defaultPosition, order)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _lastRegistryCount = GetRegistryCount();

        _tracker.Changed += OnTrackerChanged;
        AgentRegistry.ActiveSessionChanged += OnActiveSessionChanged;

        RefreshVisibleAgents();
    }

    public override string SeparatorBefore => " ";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _tracker.Changed -= OnTrackerChanged;
        AgentRegistry.ActiveSessionChanged -= OnActiveSessionChanged;
    }

    public IReadOnlyList<(SessionStateEvent State, AgentInfo Agent)> GetVisibleAgents()
        => _visible;

    public IReadOnlyDictionary<string, AgentInfo> GetAgentLookup()
        => _agentLookup;

    public int RenderAgentSegment(StringBuilder target, SessionStateEvent state, AgentInfo registryAgent)
    {
        int segWidth = 0;

        var emoji = Markup.Escape(
            AgentSettingsPersistenceLegacy.GetPersistedEmoji(registryAgent.AgentId) ?? "🤖");
        target.Append(emoji);
        target.Append(' ');
        segWidth += CharacterWidth.GetDisplayWidth(emoji) + 1;

        var color = Markup.Escape(
            AgentSettingsPersistenceLegacy.GetPersistedColor(registryAgent.AgentId) ?? ThemeProvider.Current.Tools.General.Muted);
        var displayName = FormatAgentName(registryAgent.Name);

        target.Append('[');
        target.Append(color);
        target.Append(']');
        target.Append(displayName);
        target.Append("[/]");
        target.Append(' ');
        segWidth += displayName.Length + 1;

        var statusEmoji = _tracker.GetStatusEmoji(state.SessionKey);
        target.Append(statusEmoji);
        segWidth += CharacterWidth.GetDisplayWidth(Markup.Remove(statusEmoji));

        if (_newlyOnlineAgents.Contains(state.SessionKey))
        {
            var notificationEmoji = Markup.Escape(NotificationEmoji);
            target.Append(notificationEmoji);
            segWidth += CharacterWidth.GetDisplayWidth(notificationEmoji);
        }

        return segWidth;
    }

    public bool HasNewlyOnlineAgents => _newlyOnlineAgents.Count > 0;
    public HashSet<string> NewlyOnlineAgents => _newlyOnlineAgents;

    public void RefreshVisibleAgents()
    {
        if (_disposed) return;

        try
        {
            CheckRegistryVersionBump();

            var trackedSessions = _tracker.GetTrackedSessions();
            var activeSessionKey = AgentRegistry.ActiveSessionKey;

            var states = new List<SessionStateEvent>();
            foreach (var sk in trackedSessions)
            {
                var st = _tracker.GetSessionState(sk);
                if (st is not null) states.Add(st);
            }

            PrepareVisibleAgents(states, activeSessionKey);
            DetectNewlyOnlineAgents(activeSessionKey, states);
        }
        catch { }
    }

    protected override void BuildText()
    {
        if (_disposed) return;

        try
        {
            RefreshVisibleAgents();

            if (_visible.Count == 0)
            {
                Builder.Append(NoAgentsTextMarkup);
                return;
            }

            var openPipeStyle = ThemeProvider.Current.Tools.Messages.PanelCap;
            Builder.Append($"[{openPipeStyle}]│[/]");
            bool first = true;

            foreach (var (state, registryAgent) in _visible)
            {
                if (!first)
                {
                    var pipeStyle = ThemeProvider.Current.Tools.StatusBar.SegmentPipe;
                    Builder.Append($" [{pipeStyle}]│[/] ");
                }
                first = false;

                RenderAgentSegment(Builder, state, registryAgent);
            }
        }
        catch
        {
            Builder.Append(NoAgentsTextMarkup);
        }
    }

    // ── Data Preparation ────────────────────────────────────────────────

    private void PrepareVisibleAgents(
        List<SessionStateEvent> states,
        string? activeSessionKey)
    {
        var agentList = MaterializeAgents(AgentRegistry.Agents);
        BuildAgentLookup(agentList);

        _visible.Clear();

        foreach (var st in states)
        {
            if (st is null) continue;

            // Skip subagents
            if (st.ParentSessionKey is not null || st.SpawnedBy is not null)
                continue;

            if (st.SessionKey == activeSessionKey)
                continue;

            if (!_agentLookup.TryGetValue(st.SessionKey, out var registryAgent))
                continue;

            var show = AgentSettingsPersistenceLegacy.GetPersistedShowInStatusPanel(registryAgent.AgentId);
            if (!show) continue;

            _visible.Add((st, registryAgent));
        }

        _visible.Sort(ByLastActivityDesc);
    }

    private static List<AgentInfo> MaterializeAgents(IEnumerable<AgentInfo>? agents)
    {
        if (agents is null) return new List<AgentInfo>(0);
        if (agents is List<AgentInfo> list) return list;
        return new List<AgentInfo>(agents);
    }

    private void BuildAgentLookup(List<AgentInfo> agents)
    {
        _agentLookup.Clear();
        foreach (var agent in agents)
        {
            if (agent?.SessionKey is not null)
                _agentLookup[agent.SessionKey] = agent;
        }
    }

    private static int ByLastActivityDesc(
        (SessionStateEvent State, AgentInfo Agent) a,
        (SessionStateEvent State, AgentInfo Agent) b)
    {
        long aTime = a.State?.UpdatedAt ?? a.State?.StartedAt ?? 0;
        long bTime = b.State?.UpdatedAt ?? b.State?.StartedAt ?? 0;
        return bTime.CompareTo(aTime);
    }

    // ── Newly-online detection ───────────────────────────────────────────

    private void DetectNewlyOnlineAgents(
        string? activeSessionKey,
        List<SessionStateEvent> allStates)
    {
        foreach (var (state, _) in _visible)
        {
            var currentEmoji = _tracker.GetStatusEmoji(state.SessionKey);
            if (_previousStatusEmojis.TryGetValue(state.SessionKey, out var previousEmoji))
            {
                if (previousEmoji != ReadyEmoji &&
                    currentEmoji == ReadyEmoji &&
                    state.SessionKey != activeSessionKey)
                {
                    _newlyOnlineAgents.Add(state.SessionKey);
                }
            }
            _previousStatusEmojis[state.SessionKey] = currentEmoji;
        }

        var currentKeys = new HashSet<string>(allStates.Select(s => s.SessionKey));
        var keysToRemove = _previousStatusEmojis.Keys
            .Where(k => !currentKeys.Contains(k)).ToList();
        foreach (var key in keysToRemove)
        {
            _previousStatusEmojis.Remove(key);
            _newlyOnlineAgents.Remove(key);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static string FormatAgentName(string? name)
    {
        var raw = name ?? string.Empty;
        var truncated = raw.Length > MaxAgentNameLength
            ? raw[..MaxAgentNameLength]
            : raw;
        return Markup.Escape(truncated);
    }

    private void OnTrackerChanged(string _)
    {
        RefreshVisibleAgents();
        MarkDirty();
        Changed?.Invoke();
    }

    private void OnActiveSessionChanged(string? sessionKey)
    {
        if (sessionKey is not null)
            _newlyOnlineAgents.Remove(sessionKey);
        RefreshVisibleAgents();
        MarkDirty();
        Changed?.Invoke();
    }

    private static int GetRegistryCount()
    {
        var agents = AgentRegistry.Agents;
        return agents?.Count ?? 0;
    }

    private void CheckRegistryVersionBump()
    {
        var count = GetRegistryCount();
        if (count != _lastRegistryCount)
        {
            _lastRegistryCount = count;
            MarkDirty();
            Changed?.Invoke();
        }
    }
}

using System.Text;
using OpenClawPTT.Formatting;
using Spectre.Console;

namespace OpenClawPTT.Services.StatusParts;

/// <summary>
/// Renders the main agents list as a single Spectre-markup line, e.g.
/// "│ 🤖Kimi ✅ │ 🤖Claude ⏳". Skips the active agent and subagents;
/// respects per-agent ShowInStatusPanel settings.
///
/// When placed in the <see cref="AppStatusBottomPanel"/> (via
/// <see cref="DisplayPosition.AppStatusPanelLeft"/> or
/// <see cref="DisplayPosition.AppStatusPanelRight"/>), the panel adds
/// a decorative cap line above for a 2-row look.  Everywhere else the
/// part renders as a single line.
/// </summary>
public sealed class MainAgentsPart : StatusPartBase, IDisposable
{
    /// <summary>Raised whenever the part's data changes (tracker, registry, active session).</summary>
    public event Action? Changed;

    private const int MaxAgentNameLength = 10;
    private const string NoAgentsText = "No agents connected";
    private const string NoAgentsTextMarkup = $"[grey]{NoAgentsText}[/]";

    private const string ReadyEmoji = "🟢";
    private const string NotificationEmoji = "❗";

    private readonly IAgentStatusTracker _tracker;

    // Reusable lists to avoid per-render allocations
    private readonly List<(AgentStatusSnapshot Snapshot, AgentInfo Agent)> _visible = new();
    private readonly Dictionary<string, AgentInfo> _agentLookup = new();

    // Registry-change detection
    private int _lastRegistryCount;

    // Newly-online tracking
    private readonly HashSet<string> _newlyOnlineAgents = new();
    private readonly Dictionary<string, string> _previousStatusEmojis = new();

    private bool _disposed;

    public MainAgentsPart(
        IAgentStatusTracker tracker,
        DisplayPosition defaultPosition = DisplayPosition.AppStatusPanelLeft,
        int order = 0)
        : base(defaultPosition, order)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _lastRegistryCount = GetRegistryCount();

        _tracker.Changed += OnTrackerChanged;
        AgentRegistry.ActiveSessionChanged += OnActiveSessionChanged;
    }

    /// <inheritdoc />
    public override string SeparatorBefore => " ";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _tracker.Changed -= OnTrackerChanged;
        AgentRegistry.ActiveSessionChanged -= OnActiveSessionChanged;
    }

    /// <summary>
    /// Returns the visible agents data (snapshot + registry info) used for
    /// rendering.  Exposed so <see cref="AppStatusBottomPanel"/> can re-use
    /// the same data without re-querying the tracker.
    /// </summary>
    public IReadOnlyList<(AgentStatusSnapshot Snapshot, AgentInfo Agent)> GetVisibleAgents()
    {
        return _visible;
    }

    /// <summary>
    /// Builds the agent lookup dictionary for external use
    /// (e.g. <see cref="AppStatusBottomPanel"/> segment widths).
    /// </summary>
    public IReadOnlyDictionary<string, AgentInfo> GetAgentLookup()
    {
        return _agentLookup;
    }

    /// <summary>
    /// Renders a single agent segment (emoji, name, status) into the builder
    /// and returns its display width.  Shared by both the single-line part
    /// and the <see cref="AppStatusBottomPanel"/>.
    /// </summary>
    public int RenderAgentSegment(StringBuilder target, AgentStatusSnapshot snapshot, AgentInfo registryAgent)
    {
        int segWidth = 0;

        // Emoji
        var emoji = Markup.Escape(
            AgentSettingsPersistenceLegacy.GetPersistedEmoji(registryAgent.AgentId) ?? "🤖");
        target.Append(emoji);
        target.Append(' ');
        segWidth += CharacterWidth.GetDisplayWidth(emoji) + 1;

        // Name (truncated + colorized)
        var color = Markup.Escape(
            AgentSettingsPersistenceLegacy.GetPersistedColor(registryAgent.AgentId) ?? "grey");
        var displayName = FormatAgentName(registryAgent.Name);

        target.Append('[');
        target.Append(color);
        target.Append(']');
        target.Append(displayName);
        target.Append("[/]");
        target.Append(' ');
        segWidth += displayName.Length + 1;

        // Status emoji
        var statusEmoji = Markup.Escape(snapshot.GetStatusEmoji());
        target.Append(statusEmoji);
        segWidth += CharacterWidth.GetDisplayWidth(statusEmoji);

        // Notification mark for agents that just came back online
        if (_newlyOnlineAgents.Contains(snapshot.SessionKey))
        {
            var notificationEmoji = Markup.Escape(NotificationEmoji);
            target.Append(notificationEmoji);
            segWidth += CharacterWidth.GetDisplayWidth(notificationEmoji);
        }

        return segWidth;
    }

    /// <summary>
    /// Returns whether any agents are ready for notification (newly online).
    /// Cleared when the agent is activated.
    /// </summary>
    public bool HasNewlyOnlineAgents => _newlyOnlineAgents.Count > 0;

    /// <summary>
    /// Returns the set of newly-online agent session keys for external query.
    /// </summary>
    public HashSet<string> NewlyOnlineAgents => _newlyOnlineAgents;

    protected override void BuildText()
    {
        if (_disposed) return;

        try
        {
            CheckRegistryVersionBump();

            var snapshots = _tracker.All;
            var activeSessionKey = AgentRegistry.ActiveSessionKey;

            // Prepare visible agents
            PrepareVisibleAgents(snapshots, activeSessionKey);

            // Detect newly-online transitions
            DetectNewlyOnlineAgents(activeSessionKey, snapshots);

            if (_visible.Count == 0)
            {
                Builder.Append(NoAgentsTextMarkup);
                return;
            }

            // Render the status line
            Builder.Append('│');
            bool first = true;

            foreach (var (snapshot, registryAgent) in _visible)
            {
                if (!first)
                {
                    Builder.Append(" [white bold]│[/] ");
                }
                first = false;

                RenderAgentSegment(Builder, snapshot, registryAgent);
            }
        }
        catch
        {
            Builder.Append(NoAgentsTextMarkup);
        }
    }

    // ── Data Preparation ────────────────────────────────────────────────

    private void PrepareVisibleAgents(
        IReadOnlyList<AgentStatusSnapshot>? snapshots,
        string? activeSessionKey)
    {
        var agentList = MaterializeAgents(AgentRegistry.Agents);
        BuildAgentLookup(agentList);

        _visible.Clear();

        if (snapshots is null) return;

        foreach (var snapshot in snapshots)
        {
            if (snapshot is null || snapshot.IsSubagent || snapshot.SessionKey == activeSessionKey)
                continue;

            if (!_agentLookup.TryGetValue(snapshot.SessionKey!, out var registryAgent))
                continue;

            var show = AgentSettingsPersistenceLegacy.GetPersistedShowInStatusPanel(registryAgent.AgentId);
            if (!show)
                continue;

            _visible.Add((snapshot, registryAgent));
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
        (AgentStatusSnapshot Snapshot, AgentInfo Agent) a,
        (AgentStatusSnapshot Snapshot, AgentInfo Agent) b)
    {
        long aTime = a.Snapshot.UpdatedAt ?? a.Snapshot.StartedAt ?? 0;
        long bTime = b.Snapshot.UpdatedAt ?? b.Snapshot.StartedAt ?? 0;
        return bTime.CompareTo(aTime);
    }

    // ── Newly-online detection ───────────────────────────────────────────

    private void DetectNewlyOnlineAgents(
        string? activeSessionKey,
        IReadOnlyList<AgentStatusSnapshot>? allSnapshots)
    {
        foreach (var (snapshot, _) in _visible)
        {
            var currentEmoji = snapshot.GetStatusEmoji();
            if (_previousStatusEmojis.TryGetValue(snapshot.SessionKey, out var previousEmoji))
            {
                if (previousEmoji != ReadyEmoji &&
                    currentEmoji == ReadyEmoji &&
                    snapshot.SessionKey != activeSessionKey)
                {
                    _newlyOnlineAgents.Add(snapshot.SessionKey);
                }
            }
            _previousStatusEmojis[snapshot.SessionKey] = currentEmoji;
        }

        if (allSnapshots is not null)
        {
            var currentKeys = new HashSet<string>(allSnapshots.Select(s => s.SessionKey));
            var keysToRemove = _previousStatusEmojis.Keys
                .Where(k => !currentKeys.Contains(k)).ToList();
            foreach (var key in keysToRemove)
            {
                _previousStatusEmojis.Remove(key);
                _newlyOnlineAgents.Remove(key);
            }
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

    private void OnTrackerChanged()
    {
        MarkDirty();
        Changed?.Invoke();
    }

    private void OnActiveSessionChanged(string? sessionKey)
    {
        if (sessionKey is not null)
            _newlyOnlineAgents.Remove(sessionKey);
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

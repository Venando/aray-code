using System.Text;
using Spectre.Console;
using StreamShell;

namespace OpenClawPTT.Services;

/// <summary>
/// Compact left-aligned bottom panel showing agent statuses.
/// Skips the currently active agent and sorts remaining agents by last activity.
///
/// Line count is dynamically capped: expands up to <see cref="_maxLineCount"/> when
/// content requires decorative borders, shrinks to 1 when only the status line is needed.
/// This minimises string allocations (GC pressure) during quiet periods.
/// </summary>
public sealed class AgentStatusBottomPanel : IBottomPanel, IDisposable
{
    private const int MaxAgentNameLength = 10;
    private const int DefaultLineCount = 2;
    private const string NoAgentsInfoText = "No agents connected";
    private const string NoAgentsInfoTextMarkup = $"[grey]{NoAgentsInfoText}[/]";
    private const string AgentStatusErrorText = "No agents connected";
    private const string AgentStatusErrorTextMarkup = $"[grey]{AgentStatusErrorText}[/]";

    private readonly IAgentStatusTracker _tracker;
    private readonly StringBuilder _builder = new(256);
    private readonly string[] _lines;
    private readonly object _sync = new();

    // Reusable list for visible agents — avoids per-render allocation
    private readonly List<(AgentStatusSnapshot Snapshot, AgentInfo Agent)> _visible = new();

    private bool _disposed;
    private readonly int _lineCount = DefaultLineCount;

    // Version counter: increments on every meaningful change. Rendered version tracks
    // what was last painted. IsDirty is simply (_version != _renderedVersion).
    // This replaces the fragile fingerprint hash that caused missed / spurious updates.
    private int _version;
    private int _renderedVersion;

    // Cached active session key — updated atomically with _version so GetLines never
    // reads a stale active key while the fingerprint thinks it's fresh.
    private string? _cachedActiveSessionKey;

    // Registry-change detection (AgentRegistry has no "agents changed" event)
    private int _lastRegistryCount;

    // Reusable dictionary for O(1) agent lookup — cleared each render
    private readonly Dictionary<string, AgentInfo> _agentLookup = new();

    // Reusable builder for the decorative cap line — separate from _builder to avoid interference
    private readonly StringBuilder _capBuilder = new(256);

    // Cached console width to avoid calling ConsoleMetrics.GetWindowWidth() under lock.
    // Safe staleness: the panel re-renders frequently enough that a stale width is acceptable.
    private int _cachedConsoleWidth;

    public AgentStatusBottomPanel(
        IAgentStatusTracker tracker,
        int maxLineCount = DefaultLineCount)
    {
        _tracker = tracker;
        _lineCount = Math.Max(2, maxLineCount);
        _lines = new string[_lineCount];
        _cachedConsoleWidth = ConsoleMetrics.GetWindowWidth();

        // Set version before subscribing so event handlers can safely increment it
        _version = 1;
        _tracker.Changed += OnTrackerChanged;
        AgentRegistry.ActiveSessionChanged += OnActiveSessionChanged;

        _cachedActiveSessionKey = AgentRegistry.ActiveSessionKey;
        _lastRegistryCount = GetRegistryCount();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            _tracker.Changed -= OnTrackerChanged;
            AgentRegistry.ActiveSessionChanged -= OnActiveSessionChanged;
        }

        // Clear all lines to release string references outside the lock
        Array.Clear(_lines, 0, _lines.Length);
    }

    private void OnTrackerChanged()
    {
        lock (_sync) { _version++; }
    }

    private void OnActiveSessionChanged(string? sessionKey)
    {
        lock (_sync)
        {
            _cachedActiveSessionKey = sessionKey;
            _version++;
        }
    }

    /// <summary>
    /// Returns the number of lines currently needed.
    /// Dynamically shrinks to 1 when no decorative border is required,
    /// expands up to <see cref="_maxLineCount"/> when borders are needed.
    /// </summary>
    public int LineCount => _lineCount;

    public bool IsDirty
    {
        get
        {
            lock (_sync)
            {
                if (_disposed) return false;
                CheckRegistryVersionBump();
                return _version != _renderedVersion;
            }
        }
    }

    public IReadOnlyList<string> GetLines(string currentInput)
    {
        IReadOnlyList<string> result;
        int agentListPrintIndex = _lineCount - 1;
        int capPrintIndex = _lineCount - 2;

        lock (_sync)
        {
            if (_disposed)
            {
                result = Array.Empty<string>();
                return result;
            }

            CheckRegistryVersionBump();

            // Dirty path: rebuild from tracker data
            try
            {
                _builder.Clear();
                _cachedConsoleWidth = ConsoleMetrics.GetWindowWidth();

                var snapshots = _tracker.All;
                var activeSessionKey = _cachedActiveSessionKey;

                // Prepare: materialize agents, filter visible, sort by activity
                var visibleAgents = PrepareVisibleAgents(snapshots, activeSessionKey);

                // Render: build the status line and cap line
                if (visibleAgents.Count > 0)
                {
                    var statusLine = RenderStatusLine(visibleAgents, out var segmentWidths, out var contentWidth);
                    var padding = ComputePadding(contentWidth);

                    if (padding > 0)
                        statusLine = new string(' ', padding) + statusLine;

                    _lines[agentListPrintIndex] = statusLine;
                    _lines[capPrintIndex] = RenderCapLine(segmentWidths, padding);
                }
                else
                {
                    _lines[agentListPrintIndex] = NoAgentsInfoTextMarkup;
                }
            }
            catch
            {
                _lines[agentListPrintIndex] = AgentStatusErrorTextMarkup;
            }
            finally
            {
                _renderedVersion = _version;
            }
        }

        return _lines;
    }

    // ── Data Preparation ───────────────────────────────────────────────────

    /// <summary>
    /// Builds a filtered, sorted list of visible agents.
    /// Skips: active agent, subagents, hidden agents, and agents missing from the registry.
    /// Sorted by last activity descending (most recent first).
    /// </summary>
    private List<(AgentStatusSnapshot Snapshot, AgentInfo Agent)> PrepareVisibleAgents(
        IReadOnlyList<AgentStatusSnapshot>? snapshots,
        string? activeSessionKey)
    {
        var agentList = MaterializeAgents(AgentRegistry.Agents);
        BuildAgentLookup(agentList);

        _visible.Clear();

        if (snapshots is not null)
        {
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
        }

        _visible.Sort(ByLastActivityDesc);
        return _visible;
    }

    /// <summary>Materializes the agents enumerable into a list to avoid repeated enumeration.</summary>
    private static List<AgentInfo> MaterializeAgents(IEnumerable<AgentInfo>? agents)
    {
        if (agents is null)
            return new List<AgentInfo>(0);

        if (agents is List<AgentInfo> list)
            return list;

        return new List<AgentInfo>(agents);
    }

    /// <summary>Rebuilds the agent lookup dictionary for O(1) access by session key.</summary>
    private void BuildAgentLookup(List<AgentInfo> agents)
    {
        _agentLookup.Clear();
        foreach (var agent in agents)
        {
            if (agent?.SessionKey is not null)
                _agentLookup[agent.SessionKey] = agent;
        }
    }

    /// <summary>Sort comparison: most recently active agents first.</summary>
    private static int ByLastActivityDesc(
        (AgentStatusSnapshot Snapshot, AgentInfo Agent) a,
        (AgentStatusSnapshot Snapshot, AgentInfo Agent) b)
    {
        long aTime = a.Snapshot.UpdatedAt ?? a.Snapshot.StartedAt ?? 0;
        long bTime = b.Snapshot.UpdatedAt ?? b.Snapshot.StartedAt ?? 0;
        return bTime.CompareTo(aTime);
    }

    // ── Rendering ──────────────────────────────────────────────────────────

    /// <summary>
    /// Renders the full status line for all visible agents.
    /// Returns the rendered string and outputs per-agent segment widths + total content width.
    /// </summary>
    private string RenderStatusLine(
        List<(AgentStatusSnapshot Snapshot, AgentInfo Agent)> visible,
        out List<int> segmentWidths,
        out int contentWidth)
    {
        _builder.Append('│');
        contentWidth = 1;

        segmentWidths = new List<int>(visible.Count);
        bool first = true;
        const int SeparatorChars = 3; // ' │ ' = 3 visible chars between agents

        foreach (var (snapshot, registryAgent) in visible)
        {
            if (!first)
            {
                _builder.Append(" [white bold]│[/] ");
                contentWidth += SeparatorChars;
            }

            first = false;

            int segWidth = RenderAgentSegment(snapshot, registryAgent);
            contentWidth += segWidth;
            segmentWidths.Add(segWidth);
        }

        return _builder.ToString();
    }

    /// <summary>Renders a single agent segment (emoji, name, status) and returns its display width.</summary>
    private int RenderAgentSegment(AgentStatusSnapshot snapshot, AgentInfo registryAgent)
    {
        int segWidth = 0;

        // Emoji
        var emoji = Markup.Escape(
            AgentSettingsPersistenceLegacy.GetPersistedEmoji(registryAgent.AgentId) ?? "🤖");
        _builder.Append(emoji);
        _builder.Append(' ');
        segWidth += CharacterWidth.GetDisplayWidth(emoji) + 1;

        // Name (truncated + colorized)
        var color = Markup.Escape(
            AgentSettingsPersistenceLegacy.GetPersistedColor(registryAgent.AgentId) ?? "grey");
        var displayName = FormatAgentName(registryAgent.Name);

        _builder.Append('[');
        _builder.Append(color);
        _builder.Append(']');
        _builder.Append(displayName);
        _builder.Append("[/]");
        _builder.Append(' ');
        segWidth += displayName.Length + 1;

        // Status emoji
        var statusEmoji = Markup.Escape(snapshot.GetStatusEmoji());
        _builder.Append(statusEmoji);
        segWidth += CharacterWidth.GetDisplayWidth(statusEmoji);

        return segWidth;
    }

    /// <summary>Truncates and escapes an agent name for display.</summary>
    private static string FormatAgentName(string? name)
    {
        var raw = name ?? string.Empty;
        var truncated = raw.Length > MaxAgentNameLength
            ? raw[..MaxAgentNameLength]
            : raw;
        return Markup.Escape(truncated);
    }

    /// <summary>Calculates left padding needed to align content to the right edge.</summary>
    private int ComputePadding(int contentWidth)
    {
        var padding = _cachedConsoleWidth - contentWidth - 1;
        return Math.Max(0, padding);
    }

    /// <summary>
    /// Renders the decorative cap line (╭─┬─┬─) matching agent segment widths.
    /// Prepends padding so it aligns with the status line below.
    /// </summary>
    private string RenderCapLine(List<int> segmentWidths, int padding)
    {
        _capBuilder.Clear();
        _capBuilder.Append('╭');

        for (int i = 0; i < segmentWidths.Count; i++)
        {
            _capBuilder.Append('─', segmentWidths[i]);
            if (i < segmentWidths.Count - 1)
                _capBuilder.Append("─┬─");
        }

        return new string(' ', padding) + _capBuilder.ToString();
    }

    // ── Registry change detection ──────────────────────────────────────────

    private static int GetRegistryCount()
    {
        var agents = AgentRegistry.Agents;
        if (agents is null)
            return 0;
        return agents.Count;
    }

    private void CheckRegistryVersionBump()
    {
        var count = GetRegistryCount();
        if (count != _lastRegistryCount)
        {
            _lastRegistryCount = count;
            _version++;
        }
    }
}

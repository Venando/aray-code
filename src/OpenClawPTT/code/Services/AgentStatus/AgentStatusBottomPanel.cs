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
    private const string AgentStatusErrorTextMarkup = $"[grey]No agents connected[/]";

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

                // Refresh cached console width before render
                _cachedConsoleWidth = ConsoleMetrics.GetWindowWidth();

                var snapshots = _tracker.All;
                var agents = AgentRegistry.Agents;
                var activeSessionKey = _cachedActiveSessionKey;

                // Materialize agents once to avoid triple enumeration + enumerator boxing
                List<AgentInfo> agentList;
                if (agents is null)
                {
                    agentList = new List<AgentInfo>(0);
                }
                else if (agents is List<AgentInfo> list)
                {
                    agentList = list;
                }
                else
                {
                    agentList = new List<AgentInfo>(agents);
                }

                // Build dictionary for O(1) agent lookup
                _agentLookup.Clear();
                foreach (var agent in agentList)
                {
                    if (agent?.SessionKey is not null)
                        _agentLookup[agent.SessionKey] = agent;
                }

                // Collect visible agents (skip active, skip hidden, skip subagents)
                _visible.Clear();
                if (snapshots is not null)
                {
                    foreach (var snapshot in snapshots)
                    {
                        if (snapshot is null)
                            continue;
                        if (snapshot.IsSubagent)
                            continue;
                        if (snapshot.SessionKey == activeSessionKey)
                            continue;

                        if (!_agentLookup.TryGetValue(snapshot.SessionKey!, out var registryAgent))
                            continue;

                        var show = AgentSettingsPersistenceLegacy.GetPersistedShowInStatusPanel(registryAgent.AgentId);
                        if (!show)
                            continue;

                        _visible.Add((snapshot, registryAgent));
                    }
                }

                // Sort by last activity descending (most recent first)
                _visible.Sort((a, b) =>
                {
                    long aTime = a.Snapshot.UpdatedAt ?? a.Snapshot.StartedAt ?? 0;
                    long bTime = b.Snapshot.UpdatedAt ?? b.Snapshot.StartedAt ?? 0;
                    return bTime.CompareTo(aTime);
                });

                // Build status line and track per-agent segment widths for the cap
                bool first = true;
                int contentWidth = 0;
                var segmentWidths = new List<int>(_visible.Count);
                const int separatorChars = 3; // ' │ ' = 3 visible chars between agents

                _builder.Append('│');
                contentWidth += 1;

                foreach (var (snapshot, registryAgent) in _visible)
                {
                    if (!first)
                    {
                        _builder.Append(" [white bold]│[/] ");
                        contentWidth += separatorChars;
                    }

                    first = false;

                    var emoji = Markup.Escape(
                        AgentSettingsPersistenceLegacy.GetPersistedEmoji(registryAgent.AgentId) ?? "🤖");
                    var color = Markup.Escape(
                        AgentSettingsPersistenceLegacy.GetPersistedColor(registryAgent.AgentId) ?? "grey");

                    int segWidth = 0;

                    _builder.Append(emoji);
                    _builder.Append(' ');
                    int emojiW = CharacterWidth.GetDisplayWidth(emoji);
                    segWidth += emojiW + 1;

                    var rawName = registryAgent.Name ?? string.Empty;
                    var truncatedName = rawName.Length > MaxAgentNameLength
                        ? rawName[..MaxAgentNameLength]
                        : rawName;
                    var displayName = Markup.Escape(truncatedName);

                    _builder.Append('[');
                    _builder.Append(color);
                    _builder.Append(']');
                    _builder.Append(displayName);
                    _builder.Append("[/]");
                    _builder.Append(' ');
                    segWidth += displayName.Length + 1;

                    var statusEmoji = Markup.Escape(snapshot.GetStatusEmoji());
                    _builder.Append(statusEmoji);
                    int statusW = CharacterWidth.GetDisplayWidth(statusEmoji);
                    segWidth += statusW;

                    contentWidth += segWidth;
                    segmentWidths.Add(segWidth);
                }

                if (!first)
                {
                    var width = _cachedConsoleWidth;
                    var padding = width - contentWidth - 1;

                    if (padding > 0)
                    {
                        // Single bulk insert avoids O(n) per-char shifts of the StringBuilder buffer
                        _builder.Insert(0, new string(' ', padding));
                    }

                    // Build dynamic cap: ╭─┬─┬─ with segments matching agent widths
                    _capBuilder.Clear();
                    _capBuilder.Append('╭');
                    for (int i = 0; i < segmentWidths.Count; i++)
                    {
                        _capBuilder.Append('─', segmentWidths[i]);
                        if (i < segmentWidths.Count - 1)
                        {
                            // ─┬─ replaces ' │ ' (3 chars: dash + T-junction + dash)
                            _capBuilder.Append("─┬─");
                        }
                    }

                    string capLine = _capBuilder.ToString();

                    _lines[capPrintIndex] = new string(' ', Math.Max(0, padding)) + capLine;
                }

                _lines[agentListPrintIndex] = !first
                    ? _builder.ToString()
                    : NoAgentsInfoTextMarkup;
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

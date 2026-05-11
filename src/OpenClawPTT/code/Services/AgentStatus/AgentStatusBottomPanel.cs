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
    private const int DefaultLineCount = 1;

    private readonly IAgentStatusTracker _tracker;
    private readonly IStreamShellHost _streamShellHost;
    private readonly StringBuilder _builder = new(256);
    private readonly string[] _lines;
    private readonly int _maxLineCount;
    private readonly object _sync = new();

    // Reusable list for visible agents — avoids per-render allocation
    private readonly List<(AgentStatusSnapshot Snapshot, AgentInfo Agent)> _visible = new();

    private bool _disposed;
    private int _currentLineCount = DefaultLineCount;

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

    // Command override: shows /stop /reset /new status until tracker catches up
    private string? _commandOverride;

    // Reusable dictionary for O(1) agent lookup — cleared each render
    private readonly Dictionary<string, AgentInfo> _agentLookup = new();

    // Reusable builder for the decorative cap line — separate from _builder to avoid interference
    private readonly StringBuilder _capBuilder = new(256);

    // Cached console width to avoid calling ConsoleMetrics.GetWindowWidth() under lock.
    // Safe staleness: the panel re-renders frequently enough that a stale width is acceptable.
    private int _cachedConsoleWidth;

    public AgentStatusBottomPanel(
        IStreamShellHost streamShellHost,
        IAgentStatusTracker tracker,
        int maxLineCount = DefaultLineCount)
    {
        _streamShellHost = streamShellHost;
        _tracker = tracker;
        _maxLineCount = Math.Max(1, maxLineCount);
        _lines = new string[_maxLineCount];
        _cachedConsoleWidth = GetWindowWidth();

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
    public int LineCount => _currentLineCount;

    public bool IsDirty
    {
        get
        {
            lock (_sync)
            {
                if (_disposed) return false;
                CheckRegistryVersionBump();
                return _version != _renderedVersion || _commandOverride != null;
            }
        }
    }

    public void ClearDirty()
    {
        lock (_sync)
        {
            // No-op on version tracking: GetLines is the sole authority for advancing
            // _renderedVersion. We only clear the command override here so a caller
            // that genuinely wants to dismiss a pending command can do so.
            _commandOverride = null;
        }
    }

    public IReadOnlyList<string> GetLines(string currentInput)
    {
        string? bottomSepRightText = null;
        IReadOnlyList<string> result;

        lock (_sync)
        {
            if (_disposed)
            {
                result = Array.Empty<string>();
                bottomSepRightText = null;
                goto emit;
            }

            CheckRegistryVersionBump();

            // Detect command input — overrides stale agent status until tracker updates
            var commandDisplay = TryGetCommandDisplay(currentInput);
            if (commandDisplay != null)
                _commandOverride = commandDisplay;
            else if (!string.IsNullOrEmpty(currentInput))
                _commandOverride = null; // user typing something else — clear override
            // Note: empty input (after Enter) keeps the override so the command
            // status persists until the tracker catches up.

            // Fast path: nothing changed, no command pending
            if (_version == _renderedVersion && _commandOverride == null)
            {
                result = new ArraySegment<string>(_lines, 0, _currentLineCount);
                bottomSepRightText = null;
                goto emit;
            }

            // Command pending and no new tracker data yet — show command status
            if (_version == _renderedVersion && _commandOverride != null)
            {
                _currentLineCount = DefaultLineCount;
                _lines[0] = _commandOverride;
                ClearStaleLines();
                result = new ArraySegment<string>(_lines, 0, _currentLineCount);
                bottomSepRightText = null;
                goto emit;
            }

            // Dirty path: rebuild from tracker data
            try
            {
                _commandOverride = null; // tracker updated, clear any pending command display
                _builder.Clear();

                // Refresh cached console width before render
                _cachedConsoleWidth = GetWindowWidth();

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

                // Determine if we need the decorative top cap
                bool needsCap = _maxLineCount > DefaultLineCount;

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

                    // Build dynamic cap: ╭─┬─┬─╮ with segments matching agent widths
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
                        else
                        {
                            // ╮ closes after the last segment
                            _capBuilder.Append('╮');
                        }
                    }

                    string capLine = _capBuilder.ToString();

                    if (!needsCap)
                    {
                        // Single-line mode: decoration goes via StreamShell separator
                        bottomSepRightText = capLine;
                    }
                    else
                    {
                        // Multi-line mode: top cap line is drawn above the status line
                        _lines[0] = new string(' ', Math.Max(0, padding)) + capLine;
                    }
                }

                // Write the status / "no agents" line
                // Single-line: _lines[0] = status
                // Multi-line:  _lines[0] = cap, _lines[1] = status
                if (needsCap && !first)
                {
                    _lines[1] = _builder.ToString();
                    _currentLineCount = Math.Min(2, _maxLineCount);
                }
                else
                {
                    _lines[0] = !first
                        ? _builder.ToString()
                        : "[grey]No agents connected[/]";
                    _currentLineCount = DefaultLineCount;
                }

                ClearStaleLines();
            }
            catch
            {
                // Intentionally swallowed: render failures must not crash the StreamShell loop
                _currentLineCount = DefaultLineCount;
                _lines[0] = "[red]Agent status error[/]";
                ClearStaleLines();
            }
            finally
            {
                _renderedVersion = _version;
            }

            result = new ArraySegment<string>(_lines, 0, _currentLineCount);
        }

    emit:
        // ── External calls (outside lock) ───────────────────────────────
        // _streamShellHost.SetBottomSeparator may acquire its own locks;
        // we must not hold _sync while calling it.
        if (bottomSepRightText is not null)
            _streamShellHost.SetBottomSeparator(null, bottomSepRightText, ' ');

        return result;
    }

    /// <summary>
    /// Releases string references in unused array slots so the GC can collect them.
    /// </summary>
    private void ClearStaleLines()
    {
        for (int i = _currentLineCount; i < _maxLineCount; i++)
            _lines[i] = null!;
    }

    // ── Command detection ──────────────────────────────────────────────────

    private static string? TryGetCommandDisplay(string currentInput)
    {
        if (string.IsNullOrWhiteSpace(currentInput))
            return null;

        var cmd = currentInput.Trim();

        if (cmd.Equals("/stop", StringComparison.OrdinalIgnoreCase))
            return "[yellow]⏹ Stopping agent...[/]";
        if (cmd.Equals("/reset", StringComparison.OrdinalIgnoreCase))
            return "[yellow]🔄 Resetting agent...[/]";
        if (cmd.Equals("/new", StringComparison.OrdinalIgnoreCase))
            return "[yellow]✨ New session...[/]";

        return null;
    }

    // ── Registry change detection ──────────────────────────────────────────

    private static int GetRegistryCount()
    {
        var agents = AgentRegistry.Agents;
        if (agents is null)
            return 0;

        if (agents is System.Collections.ICollection col)
            return col.Count;

        int count = 0;
        foreach (var _ in agents)
        {
            count++;
            if (count > 100) break;
        }
        return count;
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

    private static int GetWindowWidth()
    {
        try
        {
            return ConsoleMetrics.GetWindowWidth();
        }
        catch
        {
            return 120; // safe fallback
        }
    }
}

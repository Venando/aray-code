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
    private readonly IColorConsole _colorConsole;
    private readonly IStreamShellHost _streamShellHost;
    private readonly StringBuilder _builder = new(256);
    private readonly string[] _lines;
    private readonly int _maxLineCount;
    private readonly object _sync = new();

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

    public AgentStatusBottomPanel(
        IStreamShellHost streamShellHost,
        IAgentStatusTracker tracker,
        IColorConsole colorConsole,
        int maxLineCount = DefaultLineCount)
    {
        _streamShellHost = streamShellHost;
        _tracker = tracker;
        _colorConsole = colorConsole;
        _maxLineCount = Math.Max(1, maxLineCount);
        _lines = new string[_maxLineCount];
        _tracker.Changed += OnTrackerChanged;
        AgentRegistry.ActiveSessionChanged += OnActiveSessionChanged;

        _cachedActiveSessionKey = AgentRegistry.ActiveSessionKey;
        _lastRegistryCount = GetRegistryCount();
        _version = 1; // start at 1 so IsDirty is true on first check
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _tracker.Changed -= OnTrackerChanged;
        AgentRegistry.ActiveSessionChanged -= OnActiveSessionChanged;
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
        lock (_sync)
        {
            CheckRegistryVersionBump();

            int activeLine = _maxLineCount - 1;

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
                return _lines;

            // Command pending and no new tracker data yet — show command status
            if (_version == _renderedVersion && _commandOverride != null)
            {
                _currentLineCount = DefaultLineCount;
                _lines[activeLine] = _commandOverride;
                return _lines;
            }

            // Dirty path: rebuild from tracker data
            try
            {
                _commandOverride = null; // tracker updated, clear any pending command display
                _builder.Clear();

                var snapshots = _tracker.All ?? Enumerable.Empty<AgentStatusSnapshot>();
                var agents = AgentRegistry.Agents ?? Enumerable.Empty<AgentInfo>();
                var activeSessionKey = _cachedActiveSessionKey;

                // Collect visible agents (skip active, skip hidden, skip subagents)
                var visible = new List<(AgentStatusSnapshot Snapshot, AgentInfo Agent)>(agents.Count());
                foreach (var snapshot in snapshots)
                {
                    if (snapshot is null)
                        continue;
                    if (snapshot.IsSubagent)
                        continue;
                    if (snapshot.SessionKey == activeSessionKey)
                        continue;

                    var registryAgent = agents.FirstOrDefault(a => a.SessionKey == snapshot.SessionKey);
                    if (registryAgent is null)
                        continue;

                    var show = AgentSettingsPersistenceLegacy.GetPersistedShowInStatusPanel(registryAgent.AgentId);
                    if (!show)
                        continue;

                    visible.Add((snapshot, registryAgent));
                }

                // Sort by last activity descending (most recent first)
                visible.Sort((a, b) =>
                {
                    long aTime = a.Snapshot.UpdatedAt ?? a.Snapshot.StartedAt ?? 0;
                    long bTime = b.Snapshot.UpdatedAt ?? b.Snapshot.StartedAt ?? 0;
                    return bTime.CompareTo(aTime);
                });

                // Determine if we need the decorative top cap (only when multi-line is configured)
                bool needsCap = _maxLineCount > DefaultLineCount;

                // Build status line
                bool first = true;
                int count = 0;

                _builder.Append('│');
                count += 1;

                foreach (var (snapshot, registryAgent) in visible)
                {
                    if (!first)
                    {
                        _builder.Append(" [white bold]│[/] ");
                        count += 3;
                    }

                    first = false;

                    var emoji = Markup.Escape(
                        AgentSettingsPersistenceLegacy.GetPersistedEmoji(registryAgent.AgentId) ?? "🤖");
                    var color = Markup.Escape(
                        AgentSettingsPersistenceLegacy.GetPersistedColor(registryAgent.AgentId) ?? "grey");

                    _builder.Append(emoji);
                    _builder.Append(' ');
                    count += CharacterWidth.GetDisplayWidth(emoji) + 1;

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
                    count += displayName.Length + 1;

                    var statusEmoji = Markup.Escape(snapshot.GetStatusEmoji());
                    _builder.Append(statusEmoji);
                    count += CharacterWidth.GetDisplayWidth(statusEmoji);
                }

                if (!first)
                {
                    var width = ConsoleMetrics.GetWindowWidth();
                    var insertAmmount = width - count - 1;
                    for (int i = 0; i < insertAmmount; i++)
                        _builder.Insert(0, ' ');

                    if (!needsCap)
                    {
                        // Single-line mode: decoration goes via StreamShell separator
                        _streamShellHost.SetBottomSeparator(null, "╭──────────────┬───────────────┬───────────", ' ');
                    }
                    else
                    {
                        // Multi-line mode: top cap line is drawn above the status line
                        string topCap = new string(' ', insertAmmount) + "╭──────────────┬───────────────┬───────────";
                        _lines[activeLine - 1] = topCap;
                    }
                }

                // Always write the status / "no agents" line
                _lines[activeLine] = !first
                    ? _builder.ToString()
                    : "[grey]No agents connected[/]";

                // Dynamic line count: if we have visible agents and multi-line was requested,
                // use 2 lines (cap + status). Otherwise stick to 1.
                _currentLineCount = needsCap && !first ? Math.Min(2, _maxLineCount) : DefaultLineCount;

                // Clear any stale lines beyond _currentLineCount to release string references
                for (int i = _currentLineCount; i < _maxLineCount; i++)
                    _lines[i] = null!;
            }
            catch
            {
                // Intentionally swallowed: render failures must not crash the StreamShell loop
                _currentLineCount = DefaultLineCount;
                _lines[activeLine] = "[red]Agent status error[/]";
            }
            finally
            {
                _renderedVersion = _version;
            }

            return _lines;
        }
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
}

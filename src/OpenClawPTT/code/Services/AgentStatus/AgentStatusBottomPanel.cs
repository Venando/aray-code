using System.Text;
using Spectre.Console;
using StreamShell;

namespace OpenClawPTT.Services;

/// <summary>
/// Compact left-aligned bottom panel showing agent statuses.
/// Skips the currently active agent and sorts remaining agents by last activity.
/// </summary>
public sealed class AgentStatusBottomPanel : IBottomPanel, IDisposable
{
    private const int MaxLineCount = 1;
    private const int MaxAgentNameLength = 10;

    private readonly IAgentStatusTracker _tracker;
    private readonly IColorConsole _colorConsole;
    private readonly StringBuilder _builder = new(256);
    private readonly string[] _lines = new string[MaxLineCount];
    private readonly object _sync = new();

    private bool _disposed;

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

    public AgentStatusBottomPanel(IAgentStatusTracker tracker, IColorConsole colorConsole)
    {
        _tracker = tracker;
        _colorConsole = colorConsole;
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

    public int LineCount => MaxLineCount;

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
                _lines[0] = _commandOverride;
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

                // Build status line
                bool first = true;
                int count = 0;

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
                }

                _lines[0] = !first
                    ? _builder.ToString()
                    : "[grey]No agents connected[/]";
            }
            catch
            {
                // Intentionally swallowed: render failures must not crash the StreamShell loop
                _lines[0] = "[red]Agent status error[/]";
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

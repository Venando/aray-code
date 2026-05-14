using OpenClawPTT.Formatting;
using OpenClawPTT.Services.Commands;
using Spectre.Console;
using StreamShell;

namespace OpenClawPTT.Services;

/// <summary>
/// Compact bottom panel showing agents as a flat list of single-line entries.
/// Each line: bullet + name + last-action description + relative time.
///
/// Keyboard navigation:
///   Arrow Down on empty input → selection mode (AllowUserField = false)
///   Arrow Up/Down        → move selection highlight
///   Enter                → switch active agent, exit selection mode
///   Escape / Arrow Up
///     at first row       → exit selection mode, deselect everything
/// </summary>
public sealed class AgentStatusBottomPanel : IBottomPanel, IDisposable
{
    // ── Layout ───────────────────────────────────────────────────────────
    private const int BottomMargin = 0;
    private const int NameColWidth = 12;  // "• " + name (max 10)
    private const int TimeColWidth = 4;   // "12m", "1h", etc.
    private const int GapAfterName = 2;
    private const int GapBeforeTime = 2;

    private const int MaxNameDisplayLength = 10;

    // ── Dependencies ──────────────────────────────────────────────────────
    private readonly IAgentActivityStore _store;
    private readonly IConfigurationService _configService;
    private SessionHistoryService? _historyService;

    // ── State ─────────────────────────────────────────────────────────────
    private readonly object _sync = new();
    private bool _disposed;

    private int _version;
    private int _renderedVersion;

    private int _cachedLineCount;

    private bool _isSelectionMode;
    private int _selectedIndex;

    private readonly List<(string SessionKey, string? AgentId)> _visibleAgents = new();
    private string _lastCurrentInput = string.Empty;

    private readonly List<string> _lines = new(16);

    // ── Construction ──────────────────────────────────────────────────────

    public AgentStatusBottomPanel(
        IAgentActivityStore store,
        IConfigurationService configService)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));

        _version = 1;

        _store.Changed += OnStoreChanged;
    }

    // ── IBottomPanel ──────────────────────────────────────────────────────

    public int LineCount
    {
        get { lock (_sync) return _cachedLineCount; }
    }

    public bool IsDirty
    {
        get
        {
            lock (_sync)
            {
                if (_disposed) return false;
                return _version != _renderedVersion;
            }
        }
    }

    public string? CurrentSuggestion => null;
    public bool ShowBottomSeparator => true;

    public bool AllowUserField => !_isSelectionMode;

    public IReadOnlyList<string> GetLines(string currentInput)
    {
        lock (_sync)
        {
            if (_disposed) return Array.Empty<string>();

            _lines.Clear();
            _lastCurrentInput = currentInput ?? string.Empty;

            // Hide panel while user is typing
            if (_lastCurrentInput.Length > 0)
            {
                _cachedLineCount = 1;
                _renderedVersion = _version;
                return new[] { "" };
            }

            // ── Gather agents ─────────────────────────────────────────────
            var activeSessionKey = AgentRegistry.ActiveSessionKey;
            var trackedSessions = _store.GetTrackedSessions();

            _visibleAgents.Clear();

            // Active agent first (if tracked)
            if (activeSessionKey is not null && trackedSessions.Contains(activeSessionKey))
                _visibleAgents.Add((activeSessionKey, AgentRegistry.ActiveAgentId));

            // Others: all tracked sessions that aren't active and aren't subagents
            foreach (var sk in trackedSessions)
            {
                if (sk == activeSessionKey) continue;

                var state = _store.GetSessionState(sk);
                if (state is null) continue;

                // Skip subagents
                if (state.ParentSessionKey is not null || state.SpawnedBy is not null)
                    continue;

                var agent = AgentRegistry.Agents.FirstOrDefault(
                    a => a.SessionKey == sk);

                // Respect ShowInStatusPanel setting
                var show = agent is not null
                    && AgentSettingsPersistenceLegacy.GetPersistedShowInStatusPanel(agent.AgentId);
                if (!show && agent is not null) continue;

                _visibleAgents.Add((sk, agent?.AgentId));
            }

            // No agents → 0 lines
            if (_visibleAgents.Count == 0)
            {
                _cachedLineCount = 0;
                _renderedVersion = _version;
                return Array.Empty<string>();
            }

            // Clamp selection
            if (_selectedIndex >= _visibleAgents.Count)
                _selectedIndex = Math.Max(0, _visibleAgents.Count - 1);

            // ── Render ────────────────────────────────────────────────────
            for (int i = 0; i < _visibleAgents.Count; i++)
            {
                var (sessionKey, agentId) = _visibleAgents[i];
                bool selected = _isSelectionMode && i == _selectedIndex;
                bool isActive = sessionKey == activeSessionKey;

                var name = GetAgentName(agentId, sessionKey);
                var bullet = _store.GetStatusEmoji(sessionKey);
                var action = _store.GetLastActionDescription(sessionKey) ?? "[grey]…[/]";
                var timeAgo = FormatRelativeTime(_store.GetLastActivityTime(sessionKey)) ?? "…";

                _lines.Add(RenderAgentLine(name, bullet, action, timeAgo, selected));
            }

            // Hint
            _lines.Add("  [dim grey]\u2191\u2193 navigate  Enter select  Esc back[/]");

            int total = _visibleAgents.Count + 1 + BottomMargin;
            _cachedLineCount = total;
            _renderedVersion = _version;

            return PadToLineCount(total);
        }
    }

    public void ClearDirty()
    {
        lock (_sync) { _renderedVersion = _version; }
    }

    public bool TryHandleKey(ConsoleKeyInfo key)
    {
        lock (_sync)
        {
            if (!_isSelectionMode)
            {
                if (key.Key == ConsoleKey.DownArrow
                    && _visibleAgents.Count > 0
                    && _lastCurrentInput.Length == 0)
                {
                    EnterSelectionMode();
                    return true;
                }
                return false;
            }

            switch (key.Key)
            {
                case ConsoleKey.DownArrow:
                    if (_selectedIndex < _visibleAgents.Count - 1)
                    {
                        _selectedIndex++;
                        MarkDirty();
                    }
                    return true;

                case ConsoleKey.UpArrow:
                    if (_selectedIndex > 0)
                    {
                        _selectedIndex--;
                        MarkDirty();
                    }
                    else
                    {
                        ExitSelectionMode();
                    }
                    return true;

                case ConsoleKey.Enter:
                    SelectCurrentAgent();
                    return true;

                case ConsoleKey.Escape:
                    ExitSelectionMode();
                    return true;

                default:
                    ExitSelectionMode();
                    return false;
            }
        }
    }

    public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _store.Changed -= OnStoreChanged;
        _visibleAgents.Clear();
        _lines.Clear();
    }

    // ── Selection helpers ─────────────────────────────────────────────────

    private void EnterSelectionMode()
    {
        _isSelectionMode = true;
        _selectedIndex = 0;
        MarkDirty();
    }

    private void ExitSelectionMode()
    {
        _isSelectionMode = false;
        _selectedIndex = 0;
        MarkDirty();
    }

    private void SelectCurrentAgent()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _visibleAgents.Count)
        {
            ExitSelectionMode();
            return;
        }

        var (sessionKey, agentId) = _visibleAgents[_selectedIndex];
        if (agentId is null)
        {
            ExitSelectionMode();
            return;
        }

        ExitSelectionMode();

        _ = Task.Run(async () =>
        {
            await AgentRegistry.SwitchToAgentAsync(agentId, _configService, _historyService);
        });
    }

    internal void SetHistoryService(SessionHistoryService historyService)
    {
        _historyService = historyService;
    }

    // ── Rendering ─────────────────────────────────────────────────────────

    private static string RenderAgentLine(
        string name,
        string bullet,
        string action,
        string timeAgo,
        bool selected)
    {
        var consoleWidth = ConsoleMetrics.GetWindowWidth();

        // Left column: "• Name" padded to NameColWidth
        var leftCol = selected ? $"{bullet} [bold black]{name}[/]" : $"{bullet} {name}";
        int bulletWidth = CharacterWidth.GetDisplayWidth(StripMarkup(bullet));
        int leftRaw = bulletWidth + 1 + name.Length;
        int leftPad = NameColWidth - leftRaw;
        var leftPadded = leftPad > 0 ? leftCol + new string(' ', leftPad) : leftCol;

        // Action: escape + truncate
        int usedWidth = NameColWidth + GapAfterName + GapBeforeTime + TimeColWidth;
        int actionMax = consoleWidth - usedWidth;
        var actionRaw = action;
        if (actionRaw.Length > actionMax && actionMax > 3)
            actionRaw = actionRaw[..(actionMax - 1)] + "…";
        else if (actionRaw.Length > actionMax)
            actionRaw = actionRaw[..actionMax];
        var actionDisplay = Markup.Escape(actionRaw);
        int actionWidth = actionRaw.Length;
        int gapAfterAction = consoleWidth - NameColWidth - GapAfterName - actionWidth - GapBeforeTime - TimeColWidth - 1;
        if (gapAfterAction < 0) gapAfterAction = 0;

        var timePadded = timeAgo.PadLeft(TimeColWidth);

        var line = leftPadded
            + new string(' ', GapAfterName)
            + (selected ? $"[Grey23]{actionDisplay}[/]" : $"[grey]{actionDisplay}[/]")
            + new string(' ', gapAfterAction + GapBeforeTime)
            + $"[grey42]{timePadded}[/]";

        return selected ? $"[on Grey84]{line}[/]" : line;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string GetAgentName(string? agentId, string sessionKey)
    {
        if (agentId is not null)
        {
            var agent = AgentRegistry.Agents.FirstOrDefault(a => a.AgentId == agentId);
            if (agent is not null)
                return FormatName(agent.Name);
        }

        // Fall back to session state display name
        // (store is static, we access via a method)
        return FormatName(sessionKey);
    }

    private static string FormatName(string? raw)
    {
        var name = raw ?? "?";
        return name.Length > MaxNameDisplayLength
            ? Markup.Escape(name[..MaxNameDisplayLength])
            : Markup.Escape(name);
    }

    /// <summary>Formats a Unix-ms timestamp as a relative time string.</summary>
    private static string? FormatRelativeTime(long? timestampMs)
    {
        if (timestampMs is not { } ts) return null;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var diff = now - ts;

        if (diff < 0) return "now";

        var seconds = diff / 1000;
        if (seconds < 60) return $"{seconds}s";
        var minutes = seconds / 60;
        if (minutes < 60) return $"{minutes}m";
        var hours = minutes / 60;
        if (hours < 24) return $"{hours}h";
        var days = hours / 24;
        return $"{days}d";
    }

    private static string StripMarkup(string markup)
    {
        if (string.IsNullOrEmpty(markup)) return string.Empty;
        var sb = new System.Text.StringBuilder(markup.Length);
        bool inTag = false;
        for (int i = 0; i < markup.Length; i++)
        {
            char c = markup[i];
            if (c == '[' && i + 1 < markup.Length)
            {
                if (markup[i + 1] == '[') { sb.Append('['); i++; continue; }
                inTag = true;
                continue;
            }
            if (c == ']' && inTag) { inTag = false; continue; }
            if (!inTag) sb.Append(c);
        }
        return sb.ToString();
    }

    private string[] PadToLineCount(int count)
    {
        while (_lines.Count < count)
            _lines.Add(string.Empty);
        if (_lines.Count > count)
            _lines.RemoveRange(count, _lines.Count - count);
        return _lines.ToArray();
    }

    private void OnStoreChanged(string _) => MarkDirty();

    private void MarkDirty()
    {
        lock (_sync) { _version++; }
    }
}

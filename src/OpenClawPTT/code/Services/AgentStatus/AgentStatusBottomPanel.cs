using OpenClawPTT.Formatting;
using OpenClawPTT.Services.Commands;
using OpenClawPTT.Services.Themes;
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
    private readonly string[] _emptyLine = [""];
    private readonly Dictionary<int, string[]> _arraysCache = new();
    private string[] _resultArray;


    // ── Construction ──────────────────────────────────────────────────────

    public AgentStatusBottomPanel(
        IAgentActivityStore store,
        IConfigurationService configService)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _resultArray = _emptyLine;

        _version = 1;

        _store.Changed += OnStoreChanged;
        AgentRegistry.ActiveSessionChanged += OnActiveSessionChanged;
    }

    private void OnActiveSessionChanged(string? obj)
    {
        MarkDirty();
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
            if (!IsDirty && currentInput == _lastCurrentInput)
            {
                _cachedLineCount = _resultArray.Length;
                return _resultArray;
            }

            _lastCurrentInput = currentInput ?? string.Empty;

            // Hide panel while user is typing
            if (_disposed || _lastCurrentInput.Length > 0)
            {
                _cachedLineCount = 1;
                _resultArray = _emptyLine;
                return _emptyLine;
            }
            _lines.Clear();

            FillAgentsRows(_lines);

            if (_lines.Count == 0)
            {
                _cachedLineCount = 1;
                _resultArray = _emptyLine;
                return _emptyLine;
            }

            // Hint
            var hintStyle = ThemeProvider.Current.Tools.Panel.Hint;
            _lines.Add($"  [{hintStyle}]\u2191\u2193 navigate  Enter select  Esc back[/]");

            _resultArray = FillArray(_lines);
            return _resultArray;
        }
    }

    private void FillAgentsRows(List<string> lines)
    {
        // ── Gather agents ─────────────────────────────────────────────

        _visibleAgents.Clear();

        FillVisibleAgentsList(_visibleAgents);

        // No agents → 0 lines
        if (_visibleAgents.Count == 0)
        {
            return;
        }

        // Clamp selection
        if (_selectedIndex >= _visibleAgents.Count)
            _selectedIndex = Math.Max(0, _visibleAgents.Count - 1);

        // ── Render ────────────────────────────────────────────────────
        RenderAgentsLines(_visibleAgents, lines);
    }

    private void RenderAgentsLines(List<(string SessionKey, string? AgentId)> visibleAgents, List<string> lines)
    {
        var activeSessionKey = AgentRegistry.ActiveSessionKey;
        for (int i = 0; i < visibleAgents.Count; i++)
        {
            var (sessionKey, agentId) = visibleAgents[i];
            bool selected = _isSelectionMode && i == _selectedIndex;
            bool isActive = sessionKey == activeSessionKey;

            var name = GetAgentName(agentId, sessionKey);
            var bullet = _store.GetStatusEmoji(sessionKey);
            var action = new AgentActivityDescriber(_store).GetLastActionDescription(sessionKey) ?? "…";
            var timeAgo = FormatRelativeTime(_store.GetLastActivityTime(sessionKey)) ?? "…";

            lines.Add(RenderAgentLine(name, bullet, action, timeAgo, selected, isActive));
        }
    }

    private void FillVisibleAgentsList(List<(string SessionKey, string? AgentId)> visibleAgents)
    {
        var trackedSessions = _store.GetTrackedSessions();
        // Others: all tracked sessions that aren't active and aren't subagents
        foreach (var sk in trackedSessions)
        {
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

            visibleAgents.Add((sk, agent?.AgentId));
        }
    }

    private string[] FillArray(List<string> items)
    {
        _cachedLineCount = _lines.Count;

        if (_resultArray == null || _resultArray.Length != items.Count)
        {
            if (_arraysCache.TryGetValue(items.Count, out string[]? array))
            {
                items.CopyTo(array);
                return array;
            }
            else
            {
                var newArray = new string[items.Count];
                _arraysCache[items.Count] = newArray;
                items.CopyTo(newArray);
                return newArray;
            }
        }
        else
        {
            items.CopyTo(_resultArray);
            return _resultArray;
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

        AgentRegistry.ActiveSessionChanged -= OnActiveSessionChanged;
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
            return;
        }

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
        bool selected,
        bool isActive)
    {
        var consoleWidth = ConsoleMetrics.GetWindowWidth();

        // Left column: "• Name" padded to NameColWidth
        var tools = ThemeProvider.Current.Tools;
        var nameDisplay = selected
            ? $"[{tools.Panel.SelectedName}]{name}[/]"
            : name;
        nameDisplay = isActive
            ? $"[{tools.Panel.ActiveName}]{nameDisplay}[/]"
            : nameDisplay;
        var leftCol = $"{bullet} {nameDisplay}";
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

        actionDisplay = isActive
                ? $"[{tools.Panel.ActiveAgentAction}]{actionDisplay}[/]"
                : actionDisplay;

        actionDisplay = selected
                ? $"[{tools.Panel.ActionSelected}]{actionDisplay}[/]"
                : $"[{tools.Panel.Action}]{actionDisplay}[/]";

        var line = leftPadded
            + new string(' ', GapAfterName)
            + actionDisplay
            + new string(' ', gapAfterAction + GapBeforeTime)
            + $"[{tools.Panel.Time}]{timePadded}[/]";

        return selected ? $"[on {tools.Panel.SelectedBg}]{line}[/]" : line;
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

    private void OnStoreChanged(string _) => MarkDirty();

    private void MarkDirty()
    {
        lock (_sync) { _version++; }
    }
}

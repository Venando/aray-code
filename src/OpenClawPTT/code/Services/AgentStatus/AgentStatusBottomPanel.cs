using OpenClawPTT.Formatting;
using OpenClawPTT.Services.Commands;
using OpenClawPTT.Services.StatusParts;
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
    private const int BottomMargin = 1;
    private const int NameColWidth = 12;  // "• " + name (max 10)
    private const int TimeColWidth = 4;   // "12m", "1h", etc.
    private const int GapAfterName = 2;
    private const int GapBeforeTime = 2;

    // ── Hardcoded visual-test data ────────────────────────────────────────
    private static readonly string[] Actions =
    {
        "Editing src/components/Settings.tsx",
        "github.com/acme-web-app/pull",
        "Updated 4 docs pages with the new CLI flags",
        "Should the API client retry on 439, or surface the error to the caller?",
        "n/a",
        "reviewing PR #142 — auth refactor",
        "Looking at build pipeline logs",
        "Drafting release notes for v2.4",
    };

    private static readonly string[] Times =
    {
        "12m", "3m", "40m", "1h", "5m", "22m", "2h", "8m",
    };

    private const int MaxNameDisplayLength = 10;

    // ── Dependencies ──────────────────────────────────────────────────────
    private readonly IAgentStatusTracker _tracker;
    private readonly MainAgentsPart _agentsPart;
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

    private readonly List<string> _agentSessionKeys = new();
    private string _lastCurrentInput = string.Empty;

    private readonly List<string> _lines = new(16);

    // ── Construction ──────────────────────────────────────────────────────

    public AgentStatusBottomPanel(
        IAgentStatusTracker tracker,
        MainAgentsPart agentsPart,
        IConfigurationService configService)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _agentsPart = agentsPart ?? throw new ArgumentNullException(nameof(agentsPart));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));

        _version = 1;

        _tracker.Changed += OnTrackerChanged;
        AgentRegistry.ActiveSessionChanged += OnActiveSessionChanged;

        _agentsPart.RefreshVisibleAgents();
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
            _agentsPart.RefreshVisibleAgents();

            // Hide panel while user is typing
            if (_lastCurrentInput.Length > 0)
            {
                _cachedLineCount = 1;
                _renderedVersion = _version;
                return new[] { "" };
            }

            // ── Gather agents ─────────────────────────────────────────────
            var activeSessionKey = AgentRegistry.ActiveSessionKey;
            var activeSnapshot = activeSessionKey is not null
                ? _tracker.Get(activeSessionKey)
                : null;
            var activeInfo = GetActiveAgentInfo();

            var visibleAgents = _agentsPart.GetVisibleAgents();

            // Build ordered list: active first, then others
            var ordered = new List<(AgentStatusSnapshot? Snapshot, AgentInfo? Info, bool IsActive)>(visibleAgents.Count + 1);

            if (activeSnapshot is not null && activeInfo is not null)
                ordered.Add((activeSnapshot, activeInfo, true));

            foreach (var (snapshot, agent) in visibleAgents)
                ordered.Add((snapshot, agent, false));

            _agentSessionKeys.Clear();
            foreach (var (snapshot, _, _) in ordered)
            {
                if (snapshot?.SessionKey is not null)
                    _agentSessionKeys.Add(snapshot.SessionKey);
            }

            // No agents → 0 lines
            if (ordered.Count == 0)
            {
                _cachedLineCount = 0;
                _renderedVersion = _version;
                return Array.Empty<string>();
            }

            // Clamp selection
            if (_selectedIndex >= ordered.Count)
                _selectedIndex = Math.Max(0, ordered.Count - 1);

            // ── Render ────────────────────────────────────────────────────
            for (int i = 0; i < ordered.Count; i++)
            {
                var (snapshot, info, isActive) = ordered[i];
                bool selected = _isSelectionMode && i == _selectedIndex;
                _lines.Add(RenderAgentLine(snapshot, info, i, selected, isActive));
            }

            // Selection hint
            if (_isSelectionMode)
                _lines.Add("  [grey]\u2191\u2193 navigate  Enter select  Esc back[/]");

            // Line count
            int total = ordered.Count + (_isSelectionMode ? 1 : 0) + BottomMargin;
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
                    && _agentSessionKeys.Count > 0
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
                    if (_selectedIndex < _agentSessionKeys.Count - 1)
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

        _tracker.Changed -= OnTrackerChanged;
        AgentRegistry.ActiveSessionChanged -= OnActiveSessionChanged;
        _agentSessionKeys.Clear();
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
        if (_selectedIndex < 0 || _selectedIndex >= _agentSessionKeys.Count)
        {
            ExitSelectionMode();
            return;
        }

        var sessionKey = _agentSessionKeys[_selectedIndex];
        ExitSelectionMode();

        _ = Task.Run(async () =>
        {
            var visible = _agentsPart.GetVisibleAgents();
            var target = visible.FirstOrDefault(v =>
                v.Snapshot.SessionKey == sessionKey);
            if (target.Agent is not null)
            {
                await AgentRegistry.SwitchToAgentAsync(
                    target.Agent.AgentId, _configService, _historyService);
            }
        });
    }

    /// <summary>Called by AppRunner to wire history service after bootstrapping.</summary>
    internal void SetHistoryService(SessionHistoryService historyService)
    {
        _historyService = historyService;
    }

    // ── Rendering ─────────────────────────────────────────────────────────

    private static string RenderAgentLine(
        AgentStatusSnapshot? snapshot,
        AgentInfo? info,
        int index,
        bool selected,
        bool isActive)
    {
        var consoleWidth = ConsoleMetrics.GetWindowWidth();
        var name = FormatName(info?.Name);
        var bullet = snapshot is not null
            ? snapshot.GetStatusEmoji()
            : "•";

        var action = Actions[index % Actions.Length];
        var time = Times[index % Times.Length];

        // Left column: "• Name" padded to NameColWidth
        // Bullet may be Spectre markup like "[green]•[/]"; measure only the symbol.
        var leftCol = $"{bullet} {name}";
        int bulletWidth = CharacterWidth.GetDisplayWidth(StripMarkup(bullet));
        int leftRaw = bulletWidth + 1 + name.Length;
        int leftPad = NameColWidth - leftRaw;
        var leftPadded = leftPad > 0 ? leftCol + new string(' ', leftPad) : leftCol;

        // How much room is left for the action column?
        int usedWidth = NameColWidth + GapAfterName + GapBeforeTime + TimeColWidth;
        int actionMax = consoleWidth - usedWidth;

        // Action column: truncate raw, then escape for Spectre
        var actionRaw = action;
        if (actionRaw.Length > actionMax && actionMax > 3)
            actionRaw = actionRaw[..(actionMax - 1)] + "…";
        else if (actionRaw.Length > actionMax)
            actionRaw = actionRaw[..actionMax];
        var actionDisplay = Markup.Escape(actionRaw);

        int actionWidth = actionRaw.Length;
        int gapAfterAction = consoleWidth - NameColWidth - GapAfterName - actionWidth - GapBeforeTime - TimeColWidth;
        if (gapAfterAction < 0) gapAfterAction = 0;

        // Right column: pad time to be right-aligned within TimeColWidth
        var timePadded = time.PadLeft(TimeColWidth);

        var line = leftPadded
            + new string(' ', GapAfterName)
            + $"[grey]{actionDisplay}[/]"
            + new string(' ', gapAfterAction + GapBeforeTime)
            + $"[grey42]{timePadded}[/]";

        return selected
            ? $"[invert]{line}[/]"
            : line;
    }

    private static string FormatName(string? raw)
    {
        var name = raw ?? "?";
        return name.Length > MaxNameDisplayLength
            ? Markup.Escape(name[..MaxNameDisplayLength])
            : Markup.Escape(name);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private string[] PadToLineCount(int count)
    {
        while (_lines.Count < count)
            _lines.Add(string.Empty);

        if (_lines.Count > count)
            _lines.RemoveRange(count, _lines.Count - count);

        return _lines.ToArray();
    }

    private static AgentInfo? GetActiveAgentInfo()
    {
        var sessionKey = AgentRegistry.ActiveSessionKey;
        if (sessionKey is null) return null;
        return AgentRegistry.Agents.FirstOrDefault(a => a.SessionKey == sessionKey);
    }

    /// <summary>Strips Spectre markup tags for width measurement.</summary>
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

    private void OnTrackerChanged() => MarkDirty();
    private void OnActiveSessionChanged(string? _) => MarkDirty();

    private void MarkDirty()
    {
        lock (_sync) { _version++; }
    }
}

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
    // ── Throttle ────────────────────────────────────────────────────────
    private const int ThrottleIntervalMs = 3000;

    // ── Dependencies ────────────────────────────────────────────────────
    private readonly IAgentActivityStore _store;
    private readonly IConfigurationService _configService;
    private readonly VisibleAgentListBuilder _listBuilder;
    private SessionHistoryService? _historyService;

    // ── State ────────────────────────────────────────────────────────────
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

    private System.Threading.Timer? _throttleTimer;
    private bool _pendingDirty;
    private long _lastBumpTime; // Unix ms


    // ── Construction ──────────────────────────────────────────────────────

    public AgentStatusBottomPanel(
        IAgentActivityStore store,
        IConfigurationService configService)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _listBuilder = new VisibleAgentListBuilder(store);
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
        _visibleAgents.AddRange(_listBuilder.BuildVisibleAgents());

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

            var name = AgentStatusLineRenderer.GetAgentName(agentId, sessionKey);
            var bullet = _store.GetStatusEmoji(sessionKey);
            var action = new AgentActivityDescriber(_store).GetLastActionDescription(sessionKey) ?? "…";
            var timeAgo = AgentStatusLineRenderer.FormatRelativeTime(_store.GetLastActivityTime(sessionKey)) ?? "…";

            lines.Add(AgentStatusLineRenderer.RenderAgentLine(name, bullet, action, timeAgo, selected, isActive));
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
                        MarkDirtyImmediate();
                    }
                    return true;

                case ConsoleKey.UpArrow:
                    if (_selectedIndex > 0)
                    {
                        _selectedIndex--;
                        MarkDirtyImmediate();
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

        _throttleTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        _throttleTimer?.Dispose();
        _throttleTimer = null;
    }

    // ── Selection helpers ─────────────────────────────────────────────────

    private void EnterSelectionMode()
    {
        _isSelectionMode = true;
        _selectedIndex = 0;
        MarkDirtyImmediate();
    }

    private void ExitSelectionMode()
    {
        _isSelectionMode = false;
        _selectedIndex = 0;
        MarkDirtyImmediate();
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

    // ── Dirty tracking (throttled) ──────────────────────────────────────

    private void OnStoreChanged(string _) => MarkDirty();

    private void MarkDirtyImmediate()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _version++;
        }
    }

    private void MarkDirty()
    {
        lock (_sync)
        {
            if (_disposed) return;

            _pendingDirty = true;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (_throttleTimer is null)
            {
                // No timer running — decide whether to bump now or start timer
                if (_lastBumpTime == 0 || now - _lastBumpTime >= ThrottleIntervalMs)
                {
                    // Enough time has passed since last bump → bump immediately
                    _version++;
                    _lastBumpTime = now;
                    _pendingDirty = false;

                    // Start guard timer to enforce minimum spacing from now
                    _throttleTimer = new System.Threading.Timer(
                        _ => OnThrottleElapsed(),
                        null,
                        ThrottleIntervalMs,
                        System.Threading.Timeout.Infinite);
                }
                else
                {
                    // Within cooldown from last bump — start timer for remaining time
                    var remaining = ThrottleIntervalMs - (now - _lastBumpTime);
                    _throttleTimer = new System.Threading.Timer(
                        _ => OnThrottleElapsed(),
                        null,
                        remaining,
                        System.Threading.Timeout.Infinite);
                }
            }
            // If timer is already running: just set _pendingDirty — do NOT reset timer
        }
    }

    private void OnThrottleElapsed()
    {
        lock (_sync)
        {
            if (_disposed) return;

            if (_pendingDirty)
            {
                _version++;
                _lastBumpTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _pendingDirty = false;

                // Restart timer for next interval
                _throttleTimer?.Change(ThrottleIntervalMs, System.Threading.Timeout.Infinite);
            }
            else
            {
                // No pending dirty since last bump — stop timer until next MarkDirty
                _throttleTimer?.Dispose();
                _throttleTimer = null;
            }
        }
    }
}

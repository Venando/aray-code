using System.Text;
using OpenClawPTT.Formatting;
using OpenClawPTT.Services.StatusParts;
using Spectre.Console;
using StreamShell;

namespace OpenClawPTT.Services;

/// <summary>
/// Table-style bottom panel displaying agent status with columns for
/// Name, Status, Model, and Context tokens.  Organised into an "Active"
/// section (the currently selected agent) and an "Others" section
/// (remaining main agents).
///
/// Keyboard navigation:
///   Arrow Down on empty input → selection mode (AllowUserField = false)
///   Arrow Up/Down        → move selection highlight among Others
///   Enter                → switch active agent to the selected one,
///                          exit selection mode
///   Escape / Arrow Up
///     at first row       → exit selection mode, deselect everything
/// </summary>
public sealed class AgentStatusBottomPanel : IBottomPanel, IDisposable
{
    // ── Configuration ─────────────────────────────────────────────────────
    private const string HeaderTemplate = "│ [bold]Name[/]       │ [bold]Status[/] │ [bold]Model[/]      │ [bold]Context[/] │";
    private const string SectionLabelActive  = " Active";
    private const string SectionLabelOthers  = " Others";
    private const int MaxNameDisplayLength = 10;

    // ── Dependencies ──────────────────────────────────────────────────────
    private readonly IAgentStatusTracker _tracker;
    private readonly MainAgentsPart _agentsPart;

    // ── State ─────────────────────────────────────────────────────────────
    private readonly object _sync = new();
    private bool _disposed;

    private int _version;
    private int _renderedVersion;

    /// <summary>Whether keyboard selection mode is active.</summary>
    private bool _isSelectionMode;

    /// <summary>Index within the Others list currently highlighted.</summary>
    private int _selectedIndex;

    /// <summary>
    /// Ordered session keys of agents shown in the Others section.
    /// Filled during each GetLines call so TryHandleKey can map
    /// <see cref="_selectedIndex"/> to the correct session key.
    /// </summary>
    private readonly List<string> _otherSessionKeys = new();

    /// <summary>
    /// Last <c>currentInput</c> value passed to <see cref="GetLines"/>.
    /// Cached so <see cref="TryHandleKey"/> can gate selection mode
    /// entry on an empty input field.
    /// </summary>
    private string _lastCurrentInput = string.Empty;

    // ── Cached rendering ──────────────────────────────────────────────────
    private int _cachedConsoleWidth;
    private readonly List<string> _lines = new(16);

    // ── Construction ──────────────────────────────────────────────────────

    public AgentStatusBottomPanel(
        IAgentStatusTracker tracker,
        MainAgentsPart agentsPart)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _agentsPart = agentsPart ?? throw new ArgumentNullException(nameof(agentsPart));

        _cachedConsoleWidth = ConsoleMetrics.GetWindowWidth();
        _version = 1;

        _tracker.Changed += OnTrackerChanged;
        AgentRegistry.ActiveSessionChanged += OnActiveSessionChanged;

        // Trigger an initial refresh so the first GetLines has data.
        _agentsPart.RefreshVisibleAgents();
    }

    // ── IBottomPanel ──────────────────────────────────────────────────────

    public int LineCount
    {
        get
        {
            // Header (1) + empty spacer (1) = 2 base lines
            // + Section "Active" (1) + one active row (1)
            // + Section "Others" (1) + N other rows
            var visible = _agentsPart.GetVisibleAgents();
            int others = visible.Count;
            int total = 2 + 1 + 1 + 1 + others;

            // When in selection mode, reserve an extra hint line.
            if (_isSelectionMode) total++;

            return Math.Max(4, total);
        }
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

    /// <summary>No separator between input and bottom panel.</summary>
    public bool ShowBottomSeparator => false;

    /// <summary>
    /// <c>false</c> when selection mode is active (the user input field
    /// is hidden and arrow-key navigation takes over).
    /// </summary>
    public bool AllowUserField => !_isSelectionMode;

    public IReadOnlyList<string> GetLines(string currentInput)
    {
        lock (_sync)
        {
            if (_disposed) return Array.Empty<string>();

            try
            {
                _cachedConsoleWidth = ConsoleMetrics.GetWindowWidth();
                _lines.Clear();

                // Cache the current input for TryHandleKey gating
                _lastCurrentInput = currentInput ?? string.Empty;

                // Refresh data
                _agentsPart.RefreshVisibleAgents();

                // ── Build sections ────────────────────────────────────────
                var activeSessionKey = AgentRegistry.ActiveSessionKey;
                var activeSnapshot = activeSessionKey is not null
                    ? _tracker.Get(activeSessionKey)
                    : null;
                var activeInfo = GetActiveAgentInfo();

                _otherSessionKeys.Clear();
                foreach (var (snapshot, _) in _agentsPart.GetVisibleAgents())
                {
                    if (snapshot.SessionKey is not null)
                        _otherSessionKeys.Add(snapshot.SessionKey);
                }

                // Clamp selection index
                if (_selectedIndex >= _otherSessionKeys.Count)
                    _selectedIndex = Math.Max(0, _otherSessionKeys.Count - 1);

                // Emit header
                _lines.Add(HeaderTemplate);

                // Emit empty spacer
                _lines.Add(string.Empty);

                // ── Active section ────────────────────────────────────────
                _lines.Add(SectionLabelActive);
                _lines.Add(RenderActiveRow(activeSnapshot, activeInfo));

                // ── Others section ────────────────────────────────────────
                _lines.Add(SectionLabelOthers);
                int i = 0;
                foreach (var (snapshot, agent) in _agentsPart.GetVisibleAgents())
                {
                    bool isSelected = _isSelectionMode && i == _selectedIndex;
                    _lines.Add(RenderAgentRow(snapshot, agent, isSelected));
                    i++;
                }

                if (_agentsPart.GetVisibleAgents().Count == 0)
                {
                    _lines.Add("  [grey](none)[/]");
                }

                // ── Selection hint ────────────────────────────────────────
                if (_isSelectionMode)
                {
                    _lines.Add(string.Empty);
                    _lines.Add("  [grey]\u2191\u2193 navigate  Enter select  Esc back[/]");
                }
            }
            catch
            {
                _lines.Clear();
                _lines.Add("[grey]No agents connected[/]");
            }
            finally
            {
                _renderedVersion = _version;
            }

            return _lines;
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
            // ── Enter selection mode ──────────────────────────────────────
            if (!_isSelectionMode)
            {
                // Only enter selection mode when the input field is empty.
                if (key.Key == ConsoleKey.DownArrow
                    && _otherSessionKeys.Count > 0
                    && _lastCurrentInput.Length == 0)
                {
                    EnterSelectionMode();
                    return true;
                }

                return false;
            }

            // ── Navigation in selection mode ──────────────────────────────
            switch (key.Key)
            {
                case ConsoleKey.DownArrow:
                    if (_selectedIndex < _otherSessionKeys.Count - 1)
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
                        // Arrow Up at top → exit selection mode
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
                    // Any other key while in selection mode exits it.
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
        _otherSessionKeys.Clear();
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
        if (_selectedIndex < 0 || _selectedIndex >= _otherSessionKeys.Count)
        {
            ExitSelectionMode();
            return;
        }

        var sessionKey = _otherSessionKeys[_selectedIndex];
        ExitSelectionMode();

        // Switch active agent via the registry (fire-and-forget on another thread).
        _ = Task.Run(() =>
        {
            var visible = _agentsPart.GetVisibleAgents();
            var target = visible.FirstOrDefault(v =>
                v.Snapshot.SessionKey == sessionKey);
            if (target.Agent is not null)
            {
                AgentRegistry.SetActiveAgent(target.Agent.AgentId);
            }
        });
    }

    // ── Rendering helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Formats a single agent row with columns: Name, Status, Model, Context.
    /// When <paramref name="selected"/> is true, wraps the whole line in
    /// Spectre markup for highlighted background.
    /// </summary>
    private static string RenderAgentRow(
        AgentStatusSnapshot snapshot,
        AgentInfo agent,
        bool selected)
    {
        var name = FormatName(agent.Name);
        var status = Markup.Escape(snapshot.GetStatusEmoji());
        var model = FormatModel(snapshot.Model);
        var context = FormatContext(snapshot.ContextTokens);

        var row = $"│ {name,-10} │ {status,-6} │ {model,-10} │ {context,-7} │";

        return selected
            ? $"[default on gray17]{row}[/]"
            : row;
    }

    /// <summary>
    /// Renders the Active agent row.  If no active agent is found, shows
    /// a grey placeholder.  The active row is never selectable.
    /// </summary>
    private static string RenderActiveRow(
        AgentStatusSnapshot? snapshot,
        AgentInfo? info)
    {
        if (snapshot is null || info is null)
            return "  [grey](not set)[/]";

        var name = FormatName(info.Name);
        var status = Markup.Escape(snapshot.GetStatusEmoji());
        var model = FormatModel(snapshot.Model);
        var context = FormatContext(snapshot.ContextTokens);

        return $"│ {name,-10} │ {status,-6} │ {model,-10} │ {context,-7} │";
    }

    // ── Field formatters ──────────────────────────────────────────────────

    private static string FormatName(string? raw)
    {
        var name = raw ?? "?";
        return name.Length > MaxNameDisplayLength
            ? Markup.Escape(name[..MaxNameDisplayLength])
            : Markup.Escape(name);
    }

    private static string FormatModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return "[grey]…[/]";

        var display = model.Length > 10 ? model[..10] : model;
        return Markup.Escape(display);
    }

    private static string FormatContext(long? tokens)
    {
        if (tokens is null) return "[grey]…[/]";

        return tokens.Value switch
        {
            >= 1_000_000 => $"{tokens.Value / 1_000_000.0:F1}M",
            >= 1_000     => $"{tokens.Value / 1_000.0:F0}K",
            _            => tokens.Value.ToString()
        };
    }

    // ── Data helpers ──────────────────────────────────────────────────────

    private static AgentInfo? GetActiveAgentInfo()
    {
        var sessionKey = AgentRegistry.ActiveSessionKey;
        if (sessionKey is null) return null;

        return AgentRegistry.Agents.FirstOrDefault(a => a.SessionKey == sessionKey);
    }

    private void OnTrackerChanged()
    {
        MarkDirty();
    }

    private void OnActiveSessionChanged(string? _)
    {
        MarkDirty();
    }

    private void MarkDirty()
    {
        lock (_sync) { _version++; }
    }
}

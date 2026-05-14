using System.Text;
using OpenClawPTT.Formatting;
using OpenClawPTT.Services.StatusParts;
using Spectre.Console;
using StreamShell;

namespace OpenClawPTT.Services;

/// <summary>
/// Table-style bottom panel displaying agent status with columns for
/// Name, Status, Model, and Context tokens.
///
/// Keyboard navigation:
///   Arrow Down on empty input → selection mode (AllowUserField = false)
///   Arrow Up/Down        → move selection highlight among agents
///   Enter                → switch active agent to the selected one,
///                          exit selection mode
///   Escape / Arrow Up
///     at first row       → exit selection mode, deselect everything
/// </summary>
public sealed class AgentStatusBottomPanel : IBottomPanel, IDisposable
{
    // ── Margins ───────────────────────────────────────────────────────────
    private const int LeftMargin = 2;
    private const int BottomMargin = 1;
    private const string LeftPad = "  ";

    // ── Column constants ──────────────────────────────────────────────────
    private const int MinNameWidth    = 4;
    private const int MinStatusWidth  = 1;
    private const int MinModelWidth   = 3;
    private const int MinContextWidth = 3;
    private const int MaxColumnWidth  = 20;

    private const string HeaderName    = "Name";
    private const string HeaderStatus  = "Status";
    private const string HeaderModel   = "Model";
    private const string HeaderContext = "Context";

    private const int MaxNameDisplayLength = 10;

    // ── Dependencies ──────────────────────────────────────────────────────
    private readonly IAgentStatusTracker _tracker;
    private readonly MainAgentsPart _agentsPart;

    // ── State ─────────────────────────────────────────────────────────────
    private readonly object _sync = new();
    private bool _disposed;

    private int _version;
    private int _renderedVersion;

    /// <summary>
    /// Line count from the last <see cref="GetLines"/> call.
    /// Cached so <see cref="LineCount"/> always matches.
    /// </summary>
    private int _cachedLineCount;

    private bool _isSelectionMode;
    private int _selectedIndex;

    private readonly List<string> _otherSessionKeys = new();
    private string _lastCurrentInput = string.Empty;

    private readonly List<string> _lines = new(16);

    // ── Construction ──────────────────────────────────────────────────────

    public AgentStatusBottomPanel(
        IAgentStatusTracker tracker,
        MainAgentsPart agentsPart)
    {
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _agentsPart = agentsPart ?? throw new ArgumentNullException(nameof(agentsPart));

        _version = 1;

        _tracker.Changed += OnTrackerChanged;
        AgentRegistry.ActiveSessionChanged += OnActiveSessionChanged;

        _agentsPart.RefreshVisibleAgents();
    }

    // ── IBottomPanel ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns the exact number of lines produced by the last
    /// <see cref="GetLines"/> call.  Always matches.
    /// </summary>
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

            // ── Gather data ───────────────────────────────────────────────
            var activeSessionKey = AgentRegistry.ActiveSessionKey;
            var activeSnapshot = activeSessionKey is not null
                ? _tracker.Get(activeSessionKey)
                : null;
            var activeInfo = GetActiveAgentInfo();

            var visibleAgents = _agentsPart.GetVisibleAgents();
            _otherSessionKeys.Clear();
            foreach (var (snapshot, _) in visibleAgents)
            {
                if (snapshot.SessionKey is not null)
                    _otherSessionKeys.Add(snapshot.SessionKey);
            }

            if (_selectedIndex >= _otherSessionKeys.Count)
                _selectedIndex = Math.Max(0, _otherSessionKeys.Count - 1);

            // Build cell data
            var activeData = activeSnapshot is not null || activeInfo is not null
                ? CellData.FromAgent(activeSnapshot, activeInfo)
                : (CellData?)null;

            var otherData = new List<(CellData Cells, bool Selected)>(visibleAgents.Count);
            for (int i = 0; i < visibleAgents.Count; i++)
            {
                var (snapshot, agent) = visibleAgents[i];
                var cells = CellData.FromAgent(snapshot, agent);
                bool sel = _isSelectionMode && i == _selectedIndex;
                otherData.Add((cells, sel));
            }

            // ── No agents → 0 lines ──────────────────────────────────────
            if (activeData is null && otherData.Count == 0)
            {
                _cachedLineCount = 0;
                _renderedVersion = _version;
                return Array.Empty<string>();
            }

            try
            {
                // Compute column widths from all rows
                var colW = ComputeColumnWidths(activeData, otherData);

                // spacer
                _lines.Add(string.Empty);

                // Header row (only row with │)
                _lines.Add(LeftPad + RenderHeaderRow(colW));

                // spacer
                _lines.Add(string.Empty);

                // Active row
                if (activeData is not null)
                    _lines.Add(LeftPad + RenderDataRow(activeData.Value, colW, selected: false));

                // Other rows
                foreach (var (cells, selected) in otherData)
                    _lines.Add(LeftPad + RenderDataRow(cells, colW, selected));

                // Selection hint
                if (_isSelectionMode)
                    _lines.Add(LeftPad + "[grey]\u2191\u2193 navigate  Enter select  Esc back[/]");
            }
            catch
            {
                _lines.Clear();
            }
            finally
            {
                _renderedVersion = _version;
            }

            // Compute line count and pad to match
            int dataRows = (activeData is not null ? 1 : 0) + otherData.Count;
            int hint = _isSelectionMode ? 1 : 0;
            int total = 1 + 1 + 1 + dataRows + hint + BottomMargin;
            _cachedLineCount = total;

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
                    && _otherSessionKeys.Count > 0
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

        _ = Task.Run(() =>
        {
            var visible = _agentsPart.GetVisibleAgents();
            var target = visible.FirstOrDefault(v =>
                v.Snapshot.SessionKey == sessionKey);
            if (target.Agent is not null)
                AgentRegistry.SetActiveAgent(target.Agent.AgentId);
        });
    }

    // ── Column width computation ─────────────────────────────────────────

    private readonly record struct CellData(
        string Name,
        string Status,
        string Model,
        string Context
    )
    {
        public static CellData FromAgent(AgentStatusSnapshot? snapshot, AgentInfo? info)
        {
            if (snapshot is null || info is null)
                return new CellData("?", "—", "[grey]…[/]", "[grey]…[/]");

            var name = info.Name ?? "?";
            name = name.Length > MaxNameDisplayLength
                ? Markup.Escape(name[..MaxNameDisplayLength])
                : Markup.Escape(name);

            var status = Markup.Escape(snapshot.GetStatusEmoji());

            var model = !string.IsNullOrWhiteSpace(snapshot.Model)
                ? Markup.Escape(ModelPart.ShortenModelName(snapshot.Model))
                : "[grey]…[/]";

            var context = ContextPart.FormatContextDisplay(snapshot.ContextTokens, snapshot.TotalTokens)
                ?? "[grey]…[/]";

            return new CellData(name, status, model, context);
        }

        public int NameWidth    => CharacterWidth.GetDisplayWidth(StripMarkup(Name));
        public int StatusWidth  => CharacterWidth.GetDisplayWidth(StripMarkup(Status));
        public int ModelWidth   => CharacterWidth.GetDisplayWidth(StripMarkup(Model));
        public int ContextWidth => CharacterWidth.GetDisplayWidth(StripMarkup(Context));
    }

    private readonly record struct ColumnWidths(int Name, int Status, int Model, int Context);

    private static ColumnWidths ComputeColumnWidths(
        CellData? active,
        List<(CellData Cells, bool Selected)> others)
    {
        int nw = Math.Max(MinNameWidth,    Math.Min(MaxColumnWidth, HeaderName.Length));
        int sw = Math.Max(MinStatusWidth,  Math.Min(MaxColumnWidth, HeaderStatus.Length));
        int mw = Math.Max(MinModelWidth,   Math.Min(MaxColumnWidth, HeaderModel.Length));
        int cw = Math.Max(MinContextWidth, Math.Min(MaxColumnWidth, HeaderContext.Length));

        void Consider(CellData c)
        {
            nw = Math.Max(nw, Math.Min(MaxColumnWidth, c.NameWidth));
            sw = Math.Max(sw, Math.Min(MaxColumnWidth, c.StatusWidth));
            mw = Math.Max(mw, Math.Min(MaxColumnWidth, c.ModelWidth));
            cw = Math.Max(cw, Math.Min(MaxColumnWidth, c.ContextWidth));
        }

        if (active is { } a) Consider(a);
        foreach (var (cells, _) in others)
            Consider(cells);

        return new ColumnWidths(nw, sw, mw, cw);
    }

    // ── Row rendering ────────────────────────────────────────────────────

    /// <summary>Header row — the only row that uses │ separators.</summary>
    private static string RenderHeaderRow(ColumnWidths w)
    {
        return $"│ [bold]{HeaderName.PadRight(w.Name)}[/] │ [bold]{HeaderStatus.PadRight(w.Status)}[/] │ [bold]{HeaderModel.PadRight(w.Model)}[/] │ [bold]{HeaderContext.PadRight(w.Context)}[/] │";
    }

    /// <summary>Data row — no │ separators, just padded columns.</summary>
    private static string RenderDataRow(CellData cells, ColumnWidths w, bool selected)
    {
        var rawName    = StripMarkup(cells.Name);
        var rawStatus  = StripMarkup(cells.Status);
        var rawModel   = StripMarkup(cells.Model);
        var rawContext = StripMarkup(cells.Context);

        var row = $"{PadForTable(rawName, cells.Name, w.Name)}  {PadForTable(rawStatus, cells.Status, w.Status)}  {PadForTable(rawModel, cells.Model, w.Model)}  {PadForTable(rawContext, cells.Context, w.Context)}";

        return selected
            ? $"[invert]{row}[/]"
            : row;
    }

    private static string PadForTable(string raw, string markup, int targetWidth)
    {
        int currentWidth = CharacterWidth.GetDisplayWidth(raw);
        int padding = targetWidth - currentWidth;
        return padding > 0 ? markup + new string(' ', padding) : markup;
    }

    // ── Context formatting ────────────────────────────────────────────────

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string StripMarkup(string markup)
    {
        if (string.IsNullOrEmpty(markup)) return string.Empty;

        var sb = new StringBuilder(markup.Length);
        bool inTag = false;
        for (int i = 0; i < markup.Length; i++)
        {
            char c = markup[i];
            if (c == '[' && i + 1 < markup.Length)
            {
                if (markup[i + 1] == '[')
                {
                    sb.Append('[');
                    i++;
                    continue;
                }
                inTag = true;
                continue;
            }
            if (c == ']' && inTag)
            {
                inTag = false;
                continue;
            }
            if (!inTag)
                sb.Append(c);
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

    private static AgentInfo? GetActiveAgentInfo()
    {
        var sessionKey = AgentRegistry.ActiveSessionKey;
        if (sessionKey is null) return null;
        return AgentRegistry.Agents.FirstOrDefault(a => a.SessionKey == sessionKey);
    }

    private void OnTrackerChanged() => MarkDirty();
    private void OnActiveSessionChanged(string? _) => MarkDirty();

    private void MarkDirty()
    {
        lock (_sync) { _version++; }
    }
}

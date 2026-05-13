using System.Text;
using OpenClawPTT.Services.StatusParts;
using Spectre.Console;
using StreamShell;

namespace OpenClawPTT.Services;

/// <summary>
/// Bottom panel displaying the main agents list with a decorative cap line.
/// Delegates agent data preparation and segment rendering to
/// <see cref="MainAgentsPart"/> for a single-line status part, then adds a
/// decorative cap line (╭─┬─┬─) above for the 2-row panel look.
///
/// When <see cref="MainAgentsPart"/> is placed in a separator position
/// (TopSeparator*, BottomSeparator*), it renders as a single line without
/// the decorative cap — only the panel adds the cap.
/// </summary>
public sealed class AppStatusBottomPanel : IBottomPanel, IDisposable
{
    private const int DefaultLineCount = 2;
    private const string NoAgentsInfoText = "No agents connected";
    private const string NoAgentsInfoTextMarkup = $"[grey]{NoAgentsInfoText}[/]";

    private readonly MainAgentsPart _agentsPart;
    private readonly StringBuilder _capBuilder = new(256);
    private readonly string[] _lines;
    private readonly string[] _emptyLines;
    private readonly object _sync = new();

    private bool _disposed;
    private readonly int _lineCount = DefaultLineCount;

    // Version counter: increments when MainAgentsPart signals a change
    private int _version;
    private int _renderedVersion;

    // Cached console width for right-alignment
    private int _cachedConsoleWidth;

    public AppStatusBottomPanel(MainAgentsPart agentsPart, int maxLineCount = DefaultLineCount)
    {
        _agentsPart = agentsPart ?? throw new ArgumentNullException(nameof(agentsPart));
        _lineCount = Math.Max(2, maxLineCount);
        _lines = new string[_lineCount];
        _emptyLines = new string[_lineCount];
        _cachedConsoleWidth = ConsoleMetrics.GetWindowWidth();

        _version = 1;
        _agentsPart.Changed += () =>
        {
            lock (_sync) { _version++; }
        };
    }

    // Workaround: StreamShell 2026.5.12 has a bug where disposing the default bottom
    // panel crashes.
    private static bool ShouldSkipDispose() => true;

    public void Dispose()
    {
        if (ShouldSkipDispose())
            return;

        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Array.Clear(_lines, 0, _lines.Length);
    }

    public int LineCount => _lineCount;

    /// <summary>Disables the separator between input block and bottom panel.</summary>
    public bool ShowBottomSeparator => false;

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

    public IReadOnlyList<string> GetLines(string currentInput)
    {
        int agentListPrintIndex = _lineCount - 1;
        int capPrintIndex = _lineCount - 2;

        lock (_sync)
        {
            if (_disposed) return _emptyLines;

            try
            {
                _cachedConsoleWidth = ConsoleMetrics.GetWindowWidth();

                // When the MainAgentsPart is configured to None, the bottom
                // panel should be entirely empty — no "No agents connected" text.
                if (_agentsPart.Position == DisplayPosition.None)
                    return _emptyLines;

                var visible = _agentsPart.GetVisibleAgents();

                if (visible.Count > 0)
                {
                    // Build the status line via MainAgentsPart
                    var statusBuilder = new StringBuilder(256);
                    statusBuilder.Append('│');
                    var segmentWidths = new List<int>(visible.Count);
                    int contentWidth = 1;
                    bool first = true;

                    foreach (var (snapshot, registryAgent) in visible)
                    {
                        if (!first)
                        {
                            statusBuilder.Append(" [white bold]│[/] ");
                            contentWidth += 3;
                        }
                        first = false;

                        int segWidth = _agentsPart.RenderAgentSegment(statusBuilder, snapshot, registryAgent);
                        contentWidth += segWidth;
                        segmentWidths.Add(segWidth);
                    }

                    var statusLine = statusBuilder.ToString();
                    var capLine = RenderCapLine(segmentWidths);

                    _lines[agentListPrintIndex] = PadToRight(statusLine, contentWidth);
                    _lines[capPrintIndex] = PadToRight(capLine, contentWidth);
                }
                else
                {
                    _lines[agentListPrintIndex] = PadToRight(NoAgentsInfoTextMarkup, NoAgentsInfoText.Length);
                    // Clear the cap line when no agents
                    _lines[capPrintIndex] = string.Empty;
                }
            }
            catch
            {
                _lines[agentListPrintIndex] = PadToRight(NoAgentsInfoTextMarkup, NoAgentsInfoText.Length);
            }
            finally
            {
                _renderedVersion = _version;
            }
        }

        return _lines;
    }

    // ── Rendering helpers ─────────────────────────────────────────────────

    private string RenderCapLine(List<int> segmentWidths)
    {
        _capBuilder.Clear();
        _capBuilder.Append('╭');

        for (int i = 0; i < segmentWidths.Count; i++)
        {
            _capBuilder.Append('─', segmentWidths[i]);
            if (i < segmentWidths.Count - 1)
                _capBuilder.Append("─┬─");
        }

        return _capBuilder.ToString();
    }

    private int ComputePadding(int contentWidth)
    {
        var padding = _cachedConsoleWidth - contentWidth - 1;
        return Math.Max(0, padding);
    }

    private string PadToRight(string content, int contentWidth)
    {
        var padding = ComputePadding(contentWidth);
        return padding > 0
            ? new string(' ', padding) + content
            : content;
    }
}

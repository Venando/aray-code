using Spectre.Console;

namespace ArayCode.Services;

/// <summary>
/// Owns all streaming/delta formatting state for agent replies.
/// Extracted from AgentOutputAdapter to keep SRP boundaries clean.
/// Responsible for prefix printing, AgentReplyFormatter lifecycle,
/// StreamShellCapturingConsole lifecycle, and delta accumulation.
/// </summary>
public sealed class ReplyStreamCoordinator : IDisposable
{
    private readonly AppConfig _config;
    private readonly IColorConsole _console;

    private bool _prefixPrinted;
    private string _currentPrefix = "";
    private string _newlineSuffix = "";
    private int _prefixLength;
    private string _accumulatedText = "";
    private bool _isDeltaStarted;
    private IAgentReplyFormatter? _formatter;
    private StreamShellCapturingConsole? _capturingConsole;
    private bool _disposed;

    public ReplyStreamCoordinator(AppConfig config, IColorConsole console)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    /// <summary>True when a delta stream is in progress.</summary>
    public bool IsDeltaStarted => _isDeltaStarted;

    /// <summary>The accumulated text from the ongoing delta stream.</summary>
    public string AccumulatedText => _accumulatedText;

    /// <summary>Called when a delta stream begins. Resets all state.</summary>
    public void OnDeltaStart()
    {
        _isDeltaStarted = true;
        _accumulatedText = "";
        _formatter = null;
    }

    /// <summary>Called with each delta chunk during a delta stream.</summary>
    public void OnDelta(string delta)
    {
        if (!_isDeltaStarted) return;
        _accumulatedText += delta;
        EnsurePrefixPrinted();

        if (_formatter != null)
        {
            _formatter.ProcessDelta(delta);
        }
        else
        {
            _console.PrintAgentReplyDelta(_currentPrefix, delta, _newlineSuffix);
        }
    }

    /// <summary>Called when the delta stream ends. Finishes the formatter and resets state.</summary>
    public void OnDeltaEnd()
    {
        if (!_isDeltaStarted) return;
        _isDeltaStarted = false;
        _prefixPrinted = false;

        if (_formatter != null)
        {
            _formatter.Finish();
            _formatter = null;
        }

        _accumulatedText = "";
    }

    /// <summary>Called for a full (non-streaming) reply. Creates a formatter, processes the body, then finishes.</summary>
    public void OnFullReply(string body)
    {
        int consoleWidth = ConsoleMetrics.GetWindowWidth();
        int rightMargin = Math.Max(_config.RightMarginIndent, (int)(consoleWidth * 0.1));
        int availableWidth = consoleWidth - _prefixLength - rightMargin;
        if (availableWidth <= 0) availableWidth = consoleWidth / 2;

        var markdownBody = MarkdownToSpectreConverter.Convert(body, availableWidth);
        bool useCapturing = _console.GetStreamShellHost() != null;

        EnsurePrefixPrinted();

        if (_formatter != null)
        {
            _formatter.ProcessMarkupDelta(markdownBody);
            _formatter.Finish();
            _formatter = null;

            if (useCapturing && _capturingConsole != null)
            {
                _capturingConsole.FlushToStreamShell(_currentPrefix);
            }
        }
        else
        {
            _console.PrintAgentReplyWithMarkdown(_currentPrefix, markdownBody);
        }

        _prefixPrinted = false;
    }

    /// <summary>Ensures the agent prefix line has been printed before reply text.</summary>
    private void EnsurePrefixPrinted()
    {
        if (_prefixPrinted) return;
        _prefixPrinted = true;

        AgentRegistry.GetActiveNameAndEmoji(out var agentName, out var emoji);
        var color = AgentRegistry.GetActiveColor();
        var effectiveColor = color ?? AgentPersistedSettings.DefaultColor;
        var agentNameStr = agentName.ToString();
        var colorTag = $"[{effectiveColor}]";
        var colorClose = "[/]";
        var coloredName = $"{colorTag}{agentNameStr}{colorClose}";

        _currentPrefix = $"  {emoji} {coloredName}: ";
        _prefixLength = _currentPrefix.Length;
        _newlineSuffix = new string(' ', _prefixLength);

        if (_config.EnableWordWrap)
        {
            var shellHost = _console.GetStreamShellHost();
            if (shellHost != null)
            {
                _capturingConsole = new StreamShellCapturingConsole(shellHost);
                _formatter = new AgentReplyFormatter(
                    _currentPrefix, _config.ReservedRightMargin,
                    prefixAlreadyPrinted: true, output: _capturingConsole);
            }
            else
            {
                _capturingConsole = null;
                _formatter = new AgentReplyFormatter(
                    _currentPrefix, _config.ReservedRightMargin,
                    prefixAlreadyPrinted: true, output: null!);
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _formatter = null;
            _capturingConsole = null;
            _disposed = true;
        }
    }
}

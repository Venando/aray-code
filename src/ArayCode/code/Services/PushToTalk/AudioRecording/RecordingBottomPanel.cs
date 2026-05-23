using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArayCode.Services.Themes;
using Spectre.Console;
using StreamShell;

namespace ArayCode.Services;

/// <summary>
/// Stateful bottom panel for the full recording lifecycle:
/// Recording → Transcribing → Confirming/Sending → (Discarded).
/// Disables user input while active (unless AppendTranscriptionToInput mode).
/// Auto-dismisses discarded state after 1.5s.
/// 
/// The Recording state displays a voice-reactive multi-line waveform.
/// The Confirming state shows full transcribed text without truncation,
/// word-wrapped across multiple lines as needed.
/// </summary>
public sealed class RecordingBottomPanel : IBottomPanel, IDisposable
{
    public enum PanelState
    {
        Recording,
        Transcribing,
        Confirming,
        Discarded,
    }

    /// <summary>Number of lines the waveform visualization occupies in the Recording state.</summary>
    public const int WaveformLineCount = 3;

    // ── Confirming state fixed lines (prefix + instruction) ──────────────
    private const int ConfirmingFixedLines = 3; // prefix + wrapped text + instruction

    private readonly string _hotkeyCombination;
    private readonly bool _holdToTalk;
    private readonly bool _appendToInput; // true = skip confirming, append to input field
    private readonly Stopwatch _stopwatch;
    private readonly Timer _animationTimer;
    private readonly object _sync = new();

    // Optional callback to get real-time audio level (0.0–1.0, or -1 if unavailable)
    private readonly Func<float>? _getAudioLevel;

    private PanelState _state = PanelState.Recording;
    private int _animationFrame;
    private bool _isDirty = true;
    private bool _disposed;

    // Data for confirming state
    private string _transcribedText = "";
    private double _sizeKb;

    // Cached word-wrapped lines for confirming state
    private string[] _wrappedConfirmLines = [];

    // Spinner frames (Braille patterns)
    private static readonly char[] SpinnerChars = ['⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏'];

    // Unicode waveform bars (low to high) — single-character column height
    private static readonly char[] WaveformChars = ['▁', '▂', '▃', '▄', '▅', '▆', '▇', '█'];
    // Unicode half-block characters for multi-line waveform
    private static readonly char[] WaveformLowerHalf = ['▁', '▂', '▃', '▄', '▅', '▆', '▇', '█'];
    private static readonly char[] PulseChars = ['●', '◐', '◑', '◒', '◓'];

    public RecordingBottomPanel(
        string hotkeyCombination,
        bool holdToTalk,
        bool appendToInput = false,
        Func<float>? getAudioLevel = null)
    {
        _hotkeyCombination = hotkeyCombination ?? string.Empty;
        _holdToTalk = holdToTalk;
        _appendToInput = appendToInput;
        _getAudioLevel = getAudioLevel;
        _stopwatch = new Stopwatch();
        _animationTimer = new Timer(OnAnimationTick, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
        _stopwatch.Start();
    }

    // ── State transitions ───────────────────────────────────────────────

    public void SetTranscribing()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _state = PanelState.Transcribing;
            _isDirty = true;
        }
    }

    public void SetConfirming(string transcribedText, double sizeKb)
    {
        lock (_sync)
        {
            if (_disposed) return;
            _state = PanelState.Confirming;
            _transcribedText = transcribedText ?? "";
            _sizeKb = sizeKb;
            _wrappedConfirmLines = null!; // will be recomputed in GetLines
            _isDirty = true;
        }
    }

    public void SetDiscarded()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _state = PanelState.Discarded;
            _isDirty = true;
        }
    }

    // ── IBottomPanel ──────────────────────────────────────────────────

    public int LineCount
    {
        get
        {
            lock (_sync)
            {
                if (_disposed) return 0;

                return _state switch
                {
                    PanelState.Recording => 1 + WaveformLineCount, // pulse/timer/action line + waveform lines
                    PanelState.Transcribing => 1,
                    PanelState.Confirming => ConfirmingFixedLines + ComputeWrappedLineCount(),
                    PanelState.Discarded => 1,
                    _ => 0,
                };
            }
        }
    }

    public bool IsDirty
    {
        get
        {
            lock (_sync)
            {
                if (_disposed) return false;
                return _isDirty || _state == PanelState.Recording || _state == PanelState.Transcribing;
            }
        }
    }

    public string? CurrentSuggestion => null;
    public bool ShowBottomSeparator => true;
    public bool AllowUserField => _appendToInput || _state == PanelState.Confirming;

    /// <summary>
    /// When appending to input, the user can still type, but we consume
    /// all keys during the active lifecycle (recording → transcribing).
    /// During confirming, we consume keys to prevent accidental input.
    /// </summary>
    public bool TryHandleKey(ConsoleKeyInfo key)
    {
        // In append-to-input mode, only consume during recording and transcribing
        if (_appendToInput)
            return _state is PanelState.Recording or PanelState.Transcribing;

        // In normal mode, consume all keys during entire lifecycle
        return true;
    }

    public IReadOnlyList<string> GetLines(string currentInput)
    {
        lock (_sync)
        {
            if (_disposed)
                return Array.Empty<string>();

            _isDirty = false;

            return _state switch
            {
                PanelState.Recording => GetRecordingLines(),
                PanelState.Transcribing => GetTranscribingLines(),
                PanelState.Confirming => GetConfirmingLines(),
                PanelState.Discarded => GetDiscardedLines(),
                _ => Array.Empty<string>(),
            };
        }
    }

    // ── Recording state (Task 3) ────────────────────────────────────────

    private string[] GetRecordingLines()
    {
        var tools = ThemeProvider.Current.Tools;
        var recordingStyle = tools.Messages.RecordingIndicator;
        var mutedStyle = tools.General.Muted;
        var highlightStyle = tools.Messages.Highlight;

        var pulseChar = PulseChars[_animationFrame % PulseChars.Length];

        // ── Line 0: pulse + timer + action hint ────────────────────────────
        var elapsed = _stopwatch.Elapsed;
        var timerText = $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}.{elapsed.Milliseconds / 100:D1}";
        var action = _holdToTalk
            ? $"release {Markup.Escape(_hotkeyCombination)} to stop"
            : $"press {Markup.Escape(_hotkeyCombination)} again to stop";
        var line0 = $"  [{recordingStyle}]{pulseChar} REC[/]   [{highlightStyle}]{timerText}[/]   [{mutedStyle}]{action}[/]";

        // ── Waveform: N multi-line bars reacting to voice ───────────────────
        var waveformLines = BuildMultiLineWaveform();

        var lines = new string[1 + WaveformLineCount];
        lines[0] = line0;
        for (int i = 0; i < WaveformLineCount; i++)
            lines[1 + i] = $"  {waveformLines[i]}";

        return lines;
    }

    /// <summary>
    /// Builds a multi-line waveform visualization.
    /// Uses real audio level (<c>_getAudioLevel</c>) when available,
    /// otherwise falls back to a synthetic animation.
    /// Each line represents a different amplitude range, creating
    /// a stacked-bar effect: higher levels fill more rows.
    /// </summary>
    private string[] BuildMultiLineWaveform()
    {
        const int barCount = 24;
        var tools = ThemeProvider.Current.Tools;
        var activeStyle = tools.Messages.RecordingIndicator;
        var mutedStyle = tools.General.Muted;

        // Get real audio level or use synthetic animation
        float level;
        string style;
        if (_getAudioLevel != null)
        {
            float raw = _getAudioLevel();
            if (raw < 0f)
            {
                // Level monitoring unavailable — fall back to synthetic
                level = SyntheticLevel(_animationFrame);
                style = mutedStyle;
            }
            else
            {
                // Apply sqrt curve to boost low-volume responsiveness
                level = MathF.Sqrt(raw);
                // Use active style when audio is present, muted when silent
                style = raw > 0.005f ? activeStyle : mutedStyle;
            }
        }
        else
        {
            level = SyntheticLevel(_animationFrame);
            style = mutedStyle;
        }

        // Generate per-bar intensities using the audio level as amplitude
        var bars = new float[barCount];
        for (int i = 0; i < barCount; i++)
        {
            // Base shape: a moving wave influenced by the audio level
            double phase = (_animationFrame * 0.12) + (i * 0.35);
            double wave = Math.Sin(phase) * 0.35 + 0.65; // 0.3–1.0
            double perBarLevel = wave * level;

            // Add some pseudo-frequency variation so bars don't all move identically
            double variation = Math.Sin((_animationFrame * 0.25) + (i * 1.1)) * 0.12 + 0.88;
            perBarLevel *= variation;

            bars[i] = (float)Math.Clamp(perBarLevel, 0.0, 1.0);
        }

        // Build N lines where each line represents a threshold slice
        var result = new string[WaveformLineCount];
        for (int row = 0; row < WaveformLineCount; row++)
        {
            // Each row fills from bottom up: row 0 = top (highest threshold), row N-1 = bottom (lowest)
            float lowerThreshold = (WaveformLineCount - 1 - row) / (float)WaveformLineCount;
            float upperThreshold = (WaveformLineCount - row) / (float)WaveformLineCount;

            char[] lineChars = new char[barCount];
            for (int i = 0; i < barCount; i++)
            {
                float barLevel = bars[i];

                if (barLevel > lowerThreshold)
                {
                    // How far into this band we are (0–1)
                    float bandFraction = (barLevel - lowerThreshold) / (upperThreshold - lowerThreshold);
                    bandFraction = Math.Clamp(bandFraction, 0f, 1f);

                    int charIndex = (int)(bandFraction * (WaveformChars.Length - 1));
                    charIndex = Math.Clamp(charIndex, 0, WaveformChars.Length - 1);
                    lineChars[i] = WaveformChars[charIndex];
                }
                else
                {
                    lineChars[i] = ' '; // empty below threshold
                }
            }

            result[row] = $"[{style}]{new string(lineChars)}[/]";
        }

        return result;
    }

    /// <summary>
    /// Synthetic audio level for animation when real audio level is unavailable.
    /// Creates a gentle pulsing pattern so the UI doesn't go completely dead.
    /// </summary>
    private static float SyntheticLevel(int frame)
    {
        var slow = Math.Sin(frame * 0.08) * 0.3 + 0.5;
        var fast = Math.Sin(frame * 0.2) * 0.15 + 0.5;
        return (float)(slow * 0.7 + fast * 0.3);
    }

    // ── Transcribing state ─────────────────────────────────────────────

    private string[] GetTranscribingLines()
    {
        var tools = ThemeProvider.Current.Tools;
        var spinner = SpinnerChars[_animationFrame % SpinnerChars.Length];
        var highlightStyle = tools.Messages.Highlight;
        var mutedStyle = tools.General.Muted;

        return [$"  [{highlightStyle}]{spinner}[/]  [{mutedStyle}]Transcribing...[/]"];
    }

    // ── Confirming state (Task 1) ──────────────────────────────────────

    private string[] GetConfirmingLines()
    {
        var tools = ThemeProvider.Current.Tools;
        var successStyle = tools.Messages.Success;
        var mutedStyle = tools.General.Muted;
        var emphasisStyle = tools.Messages.Emphasis;

        // Prefix line
        var prefix = $"✓ Transcribed ({_sizeKb:F1} KB):";
        var line1 = $"  [{successStyle}]{prefix}[/]";

        // Transcribed text lines — full, no truncation, word-wrapped
        var wrappedLines = ComputeOrGetWrappedLines(emphasisStyle);

        // Instruction line
        var instruction = _holdToTalk
            ? $"Press {_hotkeyCombination} to send or Escape to discard"
            : $"Press {_hotkeyCombination} to send or Escape to discard";
        var lastLine = $"  [{mutedStyle}]{Markup.Escape(instruction)}[/]";

        // Combine: line1 + wrappedTranscribedLines + lastLine
        var allLines = new string[1 + wrappedLines.Length + 1];
        allLines[0] = line1;
        Array.Copy(wrappedLines, 0, allLines, 1, wrappedLines.Length);
        allLines[^1] = lastLine;

        return allLines;
    }

    /// <summary>
    /// Returns word-wrapped lines for the transcribed text.
    /// Uses a rough column estimate (console width minus indentation).
    /// Cached across calls until text changes.
    /// </summary>
    private string[] ComputeOrGetWrappedLines(string emphasisStyle)
    {
        if (_wrappedConfirmLines != null && _wrappedConfirmLines.Length > 0)
            return _wrappedConfirmLines;

        // Estimate available width: console width or default 80, minus indent
        int maxWidth = 70; // safe default
        try { maxWidth = Math.Max(40, Console.WindowWidth - 6); }
        catch { /* not a terminal, use default */ }

        var words = _transcribedText.Split(' ');
        var lines = new List<string>();
        var currentLine = new List<string>();
        int currentLen = 0;

        foreach (var word in words)
        {
            // Each line gets "  " indent + markup overhead, so available is ~maxWidth
            if (currentLen + word.Length + (currentLen > 0 ? 1 : 0) > maxWidth && currentLen > 0)
            {
                lines.Add($"  [{emphasisStyle}]{Markup.Escape(string.Join(" ", currentLine))}[/]");
                currentLine.Clear();
                currentLen = 0;
            }

            currentLine.Add(word);
            currentLen += word.Length + (currentLen > 0 ? 1 : 0);
        }

        if (currentLine.Count > 0)
            lines.Add($"  [{emphasisStyle}]{Markup.Escape(string.Join(" ", currentLine))}[/]");

        _wrappedConfirmLines = lines.ToArray();
        return _wrappedConfirmLines;
    }

    /// <summary>
    /// Computes the number of wrapped lines without emitting them (for LineCount).
    /// </summary>
    private int ComputeWrappedLineCount()
    {
        if (_wrappedConfirmLines != null && _wrappedConfirmLines.Length > 0)
            return _wrappedConfirmLines.Length;

        if (string.IsNullOrEmpty(_transcribedText))
            return 0;

        int maxWidth = 70;
        try { maxWidth = Math.Max(40, Console.WindowWidth - 6); }
        catch { /* not a terminal, use default */ }

        var words = _transcribedText.Split(' ');
        int lineCount = 0;
        int currentLen = 0;

        foreach (var word in words)
        {
            if (currentLen + word.Length + (currentLen > 0 ? 1 : 0) > maxWidth && currentLen > 0)
            {
                lineCount++;
                currentLen = word.Length;
            }
            else
            {
                currentLen += word.Length + (currentLen > 0 ? 1 : 0);
            }
        }

        if (currentLen > 0)
            lineCount++;

        return lineCount;
    }

    // ── Discarded state ───────────────────────────────────────────────

    private string[] GetDiscardedLines()
    {
        var tools = ThemeProvider.Current.Tools;
        var mutedStyle = tools.General.Muted;

        return [$"  [{mutedStyle}]─ Message discarded ─[/]"];
    }

    public void ClearDirty()
    {
        lock (_sync) { _isDirty = false; }
    }

    public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // ── Animation ─────────────────────────────────────────────────────

    private void OnAnimationTick(object? state)
    {
        lock (_sync)
        {
            if (_disposed) return;
            _animationFrame++;
            _isDirty = true;
        }
    }

    // ── Dispose ───────────────────────────────────────────────────────

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _animationTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _animationTimer?.Dispose();
        _stopwatch.Stop();
    }
}

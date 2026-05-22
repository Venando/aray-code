using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services.Themes;
using Spectre.Console;
using StreamShell;

namespace OpenClawPTT.Services;

/// <summary>
/// Stateful bottom panel for the full recording lifecycle:
/// Recording → Transcribing → Confirming/Sending → (Discarded).
/// Disables user input while active. Auto-dismisses discarded state after 1.5s.
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

    private readonly string _hotkeyCombination;
    private readonly bool _holdToTalk;
    private readonly Stopwatch _stopwatch;
    private readonly Timer _animationTimer;
    private readonly object _sync = new();

    private PanelState _state = PanelState.Recording;
    private int _animationFrame;
    private bool _isDirty = true;
    private bool _disposed;

    // Data for confirming state
    private string _transcribedText = "";
    private double _sizeKb;

    // Spinner frames (Braille patterns)
    private static readonly char[] SpinnerChars = ['⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏'];

    // Unicode waveform bars (low to high)
    private static readonly char[] WaveformChars = ['▁', '▂', '▃', '▄', '▅', '▆', '▇', '█'];
    private static readonly char[] PulseChars = ['●', '◐', '◑', '◒', '◓'];

    public RecordingBottomPanel(string hotkeyCombination, bool holdToTalk)
    {
        _hotkeyCombination = hotkeyCombination ?? string.Empty;
        _holdToTalk = holdToTalk;
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

    public int LineCount => _state switch
    {
        PanelState.Recording => 2,
        PanelState.Transcribing => 1,
        PanelState.Confirming => 3,
        PanelState.Discarded => 1,
        _ => 0,
    };

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
    public bool AllowUserField => false; // Disable user input during entire lifecycle

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

    private string[] GetRecordingLines()
    {
        var tools = ThemeProvider.Current.Tools;
        var recordingStyle = tools.Messages.RecordingIndicator;
        var mutedStyle = tools.General.Muted;
        var highlightStyle = tools.Messages.Highlight;

        var pulseChar = PulseChars[_animationFrame % PulseChars.Length];
        var elapsed = _stopwatch.Elapsed;
        var timerText = $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}.{elapsed.Milliseconds / 100:D1}";
        var waveform = BuildWaveform(_animationFrame, highlightStyle, mutedStyle);

        var line1 = $"  [{recordingStyle}]{pulseChar} REC[/]   [{highlightStyle}]{timerText}[/]   {waveform}";

        var action = _holdToTalk
            ? $"release {Markup.Escape(_hotkeyCombination)} to stop"
            : $"press {Markup.Escape(_hotkeyCombination)} again to stop";
        var line2 = $"  [{mutedStyle}]{action}[/]";

        return new[] { line1, line2 };
    }

    private string[] GetTranscribingLines()
    {
        var tools = ThemeProvider.Current.Tools;
        var spinner = SpinnerChars[_animationFrame % SpinnerChars.Length];
        var highlightStyle = tools.Messages.Highlight;
        var mutedStyle = tools.General.Muted;

        var line1 = $"  [{highlightStyle}]{spinner}[/]  [{mutedStyle}]Transcribing...[/]";
        return new[] { line1 };
    }

    private string[] GetConfirmingLines()
    {
        var tools = ThemeProvider.Current.Tools;
        var successStyle = tools.Messages.Success;
        var mutedStyle = tools.General.Muted;
        var emphasisStyle = tools.Messages.Emphasis;

        var prefix = $"✓ Transcribed ({_sizeKb:F1} KB):";
        var line1 = $"  [{successStyle}]{prefix}[/]";

        // Truncate transcribed text if too long (max ~60 chars)
        var displayText = _transcribedText.Length > 60
            ? _transcribedText[..60] + "..."
            : _transcribedText;
        var line2 = $"  [{emphasisStyle}]{Markup.Escape(displayText)}[/]";

        var instruction = _holdToTalk
            ? $"Press {_hotkeyCombination} to send or Escape to discard"
            : $"Press {_hotkeyCombination} to send or Escape to discard";
        var line3 = $"  [{mutedStyle}]{Markup.Escape(instruction)}[/]";

        return new[] { line1, line2, line3 };
    }

    private string[] GetDiscardedLines()
    {
        var tools = ThemeProvider.Current.Tools;
        var mutedStyle = tools.General.Muted;

        var line1 = $"  [{mutedStyle}]─ Message discarded ─[/]";
        return new[] { line1 };
    }

    public void ClearDirty()
    {
        lock (_sync) { _isDirty = false; }
    }

    public bool TryHandleKey(ConsoleKeyInfo key) => true; // Consume all keys during recording lifecycle

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

    private static string BuildWaveform(int frame, string activeStyle, string mutedStyle)
    {
        const int barCount = 12;
        var chars = new char[barCount];

        for (int i = 0; i < barCount; i++)
        {
            var wave = Math.Sin((frame * 0.3) + (i * 0.8)) * 0.5 + 0.5;
            var noise = Math.Sin((frame * 0.7) + (i * 1.3)) * 0.3 + 0.7;
            var intensity = wave * noise;

            int index = (int)(intensity * (WaveformChars.Length - 1));
            index = Math.Clamp(index, 0, WaveformChars.Length - 1);
            chars[i] = WaveformChars[index];
        }

        var barString = new string(chars);
        return $"[{activeStyle}]{barString}[/]";
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

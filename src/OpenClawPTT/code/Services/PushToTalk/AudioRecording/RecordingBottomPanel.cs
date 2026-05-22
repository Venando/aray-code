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
/// Animated bottom panel displayed while audio recording is in progress.
/// Shows a pulsing REC indicator, elapsed timer, simulated waveform,
/// and stop instructions. Disables user input while active.
/// </summary>
public sealed class RecordingBottomPanel : IBottomPanel, IDisposable
{
    private readonly string _hotkeyCombination;
    private readonly bool _holdToTalk;
    private readonly Stopwatch _stopwatch;
    private readonly Timer _animationTimer;
    private readonly object _sync = new();

    private int _animationFrame;
    private bool _isDirty = true;
    private bool _disposed;

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

    // ── IBottomPanel ──────────────────────────────────────────────────

    public int LineCount => 2;

    public bool IsDirty => true;

    public string? CurrentSuggestion => null;
    public bool ShowBottomSeparator => true;
    public bool AllowUserField => false; // Disable user input while recording

    public IReadOnlyList<string> GetLines(string currentInput)
    {
        lock (_sync)
        {
            if (_disposed)
                return new[] { string.Empty, string.Empty };

            var tools = ThemeProvider.Current.Tools;
            var recordingStyle = tools.Messages.RecordingIndicator;
            var mutedStyle = tools.General.Muted;
            var highlightStyle = tools.Messages.Highlight;

            // ── Line 1: pulsing dot + REC + timer + waveform ──────────
            var pulseChar = PulseChars[_animationFrame % PulseChars.Length];
            var elapsed = _stopwatch.Elapsed;
            var timerText = $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}.{elapsed.Milliseconds / 100:D1}";

            // Build animated waveform
            var waveform = BuildWaveform(_animationFrame, highlightStyle, mutedStyle);

            var line1 = $"  [{recordingStyle}]{pulseChar} REC[/]   [{highlightStyle}]{timerText}[/]   {waveform}";

            // ── Line 2: stop instruction ──────────────────────────────
            var action = _holdToTalk
                ? $"release {Markup.Escape(_hotkeyCombination)} to stop"
                : $"press {Markup.Escape(_hotkeyCombination)} again to stop";
            var line2 = $"  [{mutedStyle}]{action}[/]";

            _isDirty = false;
            return new[] { line1, line2 };
        }
    }

    public void ClearDirty()
    {
        lock (_sync) { _isDirty = false; }
    }

    public bool TryHandleKey(ConsoleKeyInfo key) => true;

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

    /// <summary>
    /// Builds a simulated waveform bar string that appears to react to voice.
    /// Uses deterministic pseudo-random based on frame + position so it
    /// animates smoothly without needing real audio levels.
    /// </summary>
    private static string BuildWaveform(int frame, string activeStyle, string mutedStyle)
    {
        const int barCount = 12;
        var chars = new char[barCount];

        for (int i = 0; i < barCount; i++)
        {
            // Deterministic "random" intensity based on frame and position
            // Creates a wave-like pattern that moves across the bars
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

    // ── Dispose ─────────────────────────────────────────────────────────

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

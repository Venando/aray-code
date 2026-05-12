using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StreamShell;

namespace OpenClawPTT.Transcriber;

/// <summary>
/// Bottom panel that displays download progress for whisper model downloads.
/// Implements <see cref="IBottomPanel"/> so it can be pushed via
/// <see cref="IStreamShellHost.SetBottomPanel"/>.
/// </summary>
public sealed class DownloadProgressBottomPanel : IBottomPanel
{
    private readonly object _sync = new();
    private readonly string[] _lines = new string[2];

    private string _fileName = "";
    private string _status = "";
    private long? _downloadedBytes;
    private long? _totalBytes;
    private bool _completed;
    private bool _isDirty = true;

    public int LineCount => 2;
    public bool IsDirty { get { lock (_sync) return _isDirty; } }
    public string? CurrentSuggestion => null;
    public bool ShowBottomSeparator => false;

    /// <summary>Updates the progress state. Thread-safe.</summary>
    public void SetProgress(string fileName, string status, long? downloadedBytes, long? totalBytes, bool completed)
    {
        lock (_sync)
        {
            _fileName = fileName;
            _status = status;
            _downloadedBytes = downloadedBytes;
            _totalBytes = totalBytes;
            _completed = completed;
            _isDirty = true;
        }
    }

    public IReadOnlyList<string> GetLines(string currentInput)
    {
        lock (_sync)
        {
            // Line 0: file name and status
            var statusText = _completed ? "[green]✓[/] " : "[cyan]⬇[/] ";
            _lines[0] = $"{statusText}[bold]{_fileName}[/] — {_status}";

            // Line 1: progress bar
            if (_completed)
            {
                var size = FormatSize(_totalBytes);
                _lines[1] = $"[green]━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━[/] {size}";
            }
            else if (_downloadedBytes.HasValue && _totalBytes.HasValue && _totalBytes.Value > 0)
            {
                var pct = (double)_downloadedBytes.Value / _totalBytes.Value;
                var filled = (int)(pct * 40);
                var downloaded = FormatSize(_downloadedBytes.Value);
                var total = FormatSize(_totalBytes.Value);
                var pctText = $"{pct * 100:F0}%";

                var filledBar = new string('━', Math.Min(filled, 40));
                var emptyBar = new string('─', Math.Max(0, 40 - filled));

                _lines[1] = $"[cyan]{filledBar}[/][grey]{emptyBar}[/] {pctText}  {downloaded} / {total}";
            }
            else
            {
                _lines[1] = "[grey]Connecting...[/]";
            }

            _isDirty = false;
            return new[] { _lines[0], _lines[1] };
        }
    }

    public void ClearDirty()
    {
        lock (_sync) { _isDirty = false; }
    }

    public bool TryHandleKey(ConsoleKeyInfo key) => false;

    public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() { /* nothing to dispose */ }

    private static string FormatSize(long? bytes)
    {
        if (bytes == null)
            return "?? MB";

        return bytes.Value switch
        {
            >= 1_000_000_000 => $"{bytes.Value / 1_000_000_000.0:F1} GB",
            >= 1_000_000 => $"{bytes.Value / 1_000_000.0:F1} MB",
            >= 1_000 => $"{bytes.Value / 1_000.0:F1} KB",
            _ => $"{bytes.Value} B"
        };
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StreamShell;

namespace OpenClawPTT.TTS.Providers;

/// <summary>
/// Simple bottom panel showing uv environment setup progress during
/// Coqui TTS model list fetch. Displays status text and the latest
/// uv stderr/stdout line.
/// </summary>
public sealed class CoquiEnvSetupPanel : IBottomPanel
{
    private readonly object _sync = new();
    private readonly string[] _lines = new string[3];

    private string _status = "Initializing...";
    private string _latestLine = "";
    private string _errorDetail = "";
    private bool _isDirty = true;

    public int LineCount => 3;
    public bool IsDirty { get { lock (_sync) return _isDirty; } }
    public string? CurrentSuggestion => null;
    public bool ShowBottomSeparator => false;

    public void SetStatus(string status, string? latestLine = null, string? errorDetail = null)
    {
        lock (_sync)
        {
            _status = status;
            if (latestLine != null)
                _latestLine = latestLine.Trim();
            if (errorDetail != null)
                _errorDetail = errorDetail.Trim();
            _isDirty = true;
        }
    }

    public void SetCompleted(bool success, string message)
    {
        lock (_sync)
        {
            _status = success
                ? $"[green]\u2713[/] {message}"
                : $"[yellow]\u26a0[/] {message}";
            _isDirty = true;
        }
    }

    public IReadOnlyList<string> GetLines(string currentInput)
    {
        lock (_sync)
        {
            _lines[0] = $"[bold]Coqui TTS Setup[/] \u2014 {_status}";
            _lines[1] = string.IsNullOrEmpty(_latestLine)
                ? "[grey]Waiting for uv...[/]"
                : $"[grey]{CoquiMarkupHelper.EscapeMarkup(_latestLine)}[/]";
            _lines[2] = string.IsNullOrEmpty(_errorDetail)
                ? ""
                : $"[yellow]{CoquiMarkupHelper.EscapeMarkup(_errorDetail)}[/]";
            _isDirty = false;
            return _lines;
        }
    }

    public void ClearDirty()
    {
        lock (_sync) { _isDirty = false; }
    }

    public bool TryHandleKey(ConsoleKeyInfo key) => false;

    public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() { }
}

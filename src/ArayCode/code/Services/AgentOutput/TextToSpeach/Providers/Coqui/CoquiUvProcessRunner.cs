using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArayCode.Services;

namespace ArayCode.TTS.Providers;

/// <summary>
/// Manages the long-running <c>uv run python tts_service.py</c> subprocess
/// lifecycle. Handles process startup, exponential backoff retry, line I/O,
/// and read-loop dispatch. Keeps <see cref="CoquiUvTtsProvider"/> focused on
/// synthesis + protocol dispatch (SRP).
/// </summary>
internal sealed class CoquiUvProcessRunner : IDisposable
{
    private readonly CoquiUvEnvironment _environment;
    private readonly IColorConsole _console;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending;
    private readonly TimeSpan _startupTimeout;
    private readonly Action<string>? _onProtocolLine;
    private readonly Action<string>? _onLogLine;

    private Process? _process;
    private CancellationTokenSource? _readCts;
    private bool _startupFailed;
    private int _consecutiveRestarts;
    private bool _disposed;

    private const int MaxConsecutiveRestarts = 3;
    private static readonly TimeSpan MinRestartDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRestartDelay = TimeSpan.FromSeconds(32);

    public bool IsRunning => _process is { HasExited: false };

    public event Action<Exception>? OnFatalError;

    public CoquiUvProcessRunner(
        CoquiUvEnvironment environment,
        IColorConsole console,
        ConcurrentDictionary<string, TaskCompletionSource<string>> pending,
        Action<string>? onProtocolLine = null,
        Action<string>? onLogLine = null,
        TimeSpan? startupTimeout = null)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _pending = pending ?? throw new ArgumentNullException(nameof(pending));
        _onProtocolLine = onProtocolLine;
        _onLogLine = onLogLine;
        _startupTimeout = startupTimeout ?? TimeSpan.FromSeconds(120);
    }

    /// <summary>
    /// Ensures the uv subprocess is running. Starts it if not already running,
    /// retrying with exponential backoff on failure.
    /// </summary>
    public async Task EnsureRunningAsync(CancellationToken ct)
    {
        if (IsRunning) return;
        if (_startupFailed)
            throw new InvalidOperationException("Coqui TTS (uv) provider is not available (previous startup failure).");

        // Verify uv is available
        if (!CoquiUvEnvironment.IsUvAvailable())
            throw new InvalidOperationException(
                $"uv is not installed. Install it with: {CoquiUvEnvironment.GetInstallInstructions()}");

        // Short-circuit if environment is known broken
        if (CoquiUvEnvironment.IsUvBuildBroken)
        {
            _startupFailed = true;
            var detail = CoquiUvEnvironment.UvBuildErrorDetail ?? "environment is broken";
            throw new InvalidOperationException(
                $"Coqui TTS (uv) environment is broken: {detail}. Fix the issue and restart.");
        }

        while (_consecutiveRestarts < MaxConsecutiveRestarts)
        {
            if (_consecutiveRestarts > 0)
            {
                var delay = TimeSpan.FromTicks(Math.Min(
                    MinRestartDelay.Ticks << (_consecutiveRestarts - 1), MaxRestartDelay.Ticks));
                _console.Log("coqui_uv_tts", $"Waiting {delay.TotalSeconds}s before retry #{_consecutiveRestarts + 1}...");
                await Task.Delay(delay, ct);
            }

            CleanupProcess();

            _readCts?.Cancel();
            _readCts?.Dispose();
            _readCts = new CancellationTokenSource();

            var psi = _environment.CreateProcessStartInfo();
            CoquiUvEnvironment.EnsureVenvPythonMatches(_environment.ProjectDir);

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.Exited += OnProcessExited;

            _console.Log("coqui_uv_tts", $"Starting: {psi.FileName} {psi.Arguments}");
            _process.Start();
            _console.Log("coqui_uv_tts", $"Started (PID: {_process.Id}).");

            if (_process.HasExited)
            {
                var exitCode = _process.ExitCode;
                _process.Dispose();
                _process = null;
                _consecutiveRestarts++;
                _console.PrintWarning(
                    $"Coqui TTS (uv) exited immediately (code: {exitCode}). " +
                    $"Attempt {_consecutiveRestarts}/{MaxConsecutiveRestarts}.");
                continue;
            }

            // Wait for {"type":"ready"}
            using var timeoutCts = new CancellationTokenSource(_startupTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            string? readyLine = null;
            bool gotErrorBeforeReady = false;

            // Read stderr concurrently during startup so we capture Python logs/tracebacks
            var startupStderr = new List<string>();
            using var stderrCts = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
            var stderrTask = Task.Run(async () =>
            {
                var reader = _process.StandardError;
                while (!stderrCts.Token.IsCancellationRequested)
                {
                    var line = await ReadLineAsync(reader, stderrCts.Token);
                    if (line == null) break;
                    startupStderr.Add(line);
                    _console.Log("coqui_uv_tts", $"[stderr] {line}");
                }
            }, stderrCts.Token);

            // Buffer for non-JSON stdout lines (e.g. TTS download progress)
            var startupStdout = new List<string>();

            while (!linked.Token.IsCancellationRequested)
            {
                readyLine = await ReadLineAsync(_process.StandardOutput, linked.Token);
                if (readyLine == null) break;

                if (TryParseType(readyLine, out var msgType))
                {
                    if (msgType == "ready") break;
                    if (msgType == "error")
                    {
                        var errMsg = TryExtractField(readyLine, "msg") ?? "unknown";
                        _process.Kill(true);
                        _process.Dispose();
                        _process = null;
                        gotErrorBeforeReady = true;
                        _consecutiveRestarts++;

                        // Build detailed error with stderr context
                        var stderrContext = BuildStderrContext(startupStderr);
                        _console.PrintWarning(
                            $"Coqui TTS (uv) startup failed: {errMsg}.{stderrContext}" +
                            $"  Attempt {_consecutiveRestarts}/{MaxConsecutiveRestarts}.");
                        break;
                    }
                }
                else
                {
                    // Non-JSON line — likely TTS library stdout (download progress, etc.)
                    startupStdout.Add(readyLine);
                    _console.Log("coqui_uv_tts", $"[startup] {readyLine}");
                }
            }

            // Cancel the startup stderr reader
            stderrCts.Cancel();
            try { await stderrTask; } catch (OperationCanceledException) { }

            if (readyLine != null && TryParseType(readyLine, out var rt) && rt == "ready" && _process != null)
            {
                _consecutiveRestarts = 0;
                StartReadLoops();
                return;
            }

            if (_process != null) { _process.Kill(true); _process.Dispose(); _process = null; }
            if (!gotErrorBeforeReady)
            {
                _consecutiveRestarts++;

                // Build detailed error with stderr and startup stdout context
                var stderrContext = BuildStderrContext(startupStderr);
                var stdoutContext = startupStdout.Count > 0
                    ? $"\n  Stdout (last 3): {string.Join(" | ", startupStdout.TakeLast(3))}"
                    : "";
                var truncatedReadyLine = (readyLine ?? "(null)");
                if (truncatedReadyLine.Length > 80)
                    truncatedReadyLine = truncatedReadyLine[..80] + "...";

                _console.PrintWarning(
                    $"Coqui TTS (uv) failed to start (expected 'ready', got: '{truncatedReadyLine}')." +
                    $"{stderrContext}{stdoutContext}" +
                    $"  Attempt {_consecutiveRestarts}/{MaxConsecutiveRestarts}.");
            }
        }

        _startupFailed = true;
        _consecutiveRestarts = 0;
        CoquiUvEnvironment.MarkUvBuildBroken(
            $"Coqui TTS (uv) failed to start after {MaxConsecutiveRestarts} attempts");
        throw new InvalidOperationException($"Coqui TTS (uv) failed to start after {MaxConsecutiveRestarts} attempts.");
    }

    /// <summary>
    /// Starts read loops for stdout and stderr. Called automatically on
    /// successful startup, and on reconnect if the process is restarted.
    /// </summary>
    private void StartReadLoops()
    {
        if (_readCts == null || _process == null || _onProtocolLine == null) return;
        var ct = _readCts.Token;

        _ = Task.Run(() => ReadLoopAsync(_process.StandardOutput, _onProtocolLine, ct), ct);
        if (_onLogLine != null)
            _ = Task.Run(() => ReadLoopAsync(_process.StandardError, _onLogLine, ct), ct);
    }

    /// <summary>
    /// Writes a JSON line to the subprocess stdin. Throws if the write times out.
    /// </summary>
    public async Task WriteRequestAsync(string jsonLine, CancellationToken ct)
    {
        var process = _process ?? throw new InvalidOperationException("Process not running.");
        await process.StandardInput.WriteAsync(jsonLine.AsMemory(), ct);
        await process.StandardInput.FlushAsync();
    }

    /// <summary>Gracefully stops the process by sending EXIT, then kills if it doesn't stop.</summary>
    public async Task StopAsync(TimeSpan? gracePeriod = null)
    {
        if (_process == null || _process.HasExited) return;

        try
        {
            var exitTask = Task.Run(async () =>
            {
                await _process.StandardInput.WriteAsync("EXIT\n");
                await _process.StandardInput.FlushAsync();
            });
            var grace = gracePeriod ?? TimeSpan.FromSeconds(2);
            using var exitCts = new CancellationTokenSource(grace);
            var winner = await Task.WhenAny(exitTask, Task.Delay(grace, exitCts.Token));
            if (winner != exitTask) { /* fall through to kill */ }
            else await exitTask;
        }
        catch { }

        try
        {
            using var killCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await _process.WaitForExitAsync(killCts.Token); }
            catch (OperationCanceledException) { _process.Kill(true); }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CleanupProcess();
        _readCts?.Dispose();
    }

    // ── Line IO ─────────────────────────────────────────────────────

    internal static async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken ct)
    {
        try { return await reader.ReadLineAsync(ct); }
        catch (OperationCanceledException) { return null; }
    }

    private async Task ReadLoopAsync(StreamReader reader, Action<string> onLine, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _process is { HasExited: false })
            {
                var line = await ReadLineAsync(reader, ct);
                if (string.IsNullOrWhiteSpace(line)) break;
                onLine(line);
            }
        }
        catch (OperationCanceledException) { }
    }

    // ── JSON helpers ────────────────────────────────────────────────

    internal static bool TryParseType(string line, out string type)
    {
        type = "";

        // Fast pre-check: non-JSON lines (TTS download progress, etc.) 
        // would throw JsonReaderException. Skip them entirely.
        if (string.IsNullOrEmpty(line) || line[0] != '{')
            return false;

        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("type", out var t))
            {
                type = t.GetString() ?? "";
                return true;
            }
        }
        catch
        {
            // Shouldn't reach here with the '{' pre-check,
            // but keep the catch for malformed JSON.
        }
        return false;
    }

    internal static string? TryExtractField(string line, string field)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.TryGetProperty(field, out var v) ? v.GetString() : null;
        }
        catch { return null; }
    }

    // ── Diagnostic helpers ───────────────────────────────────────────

    /// <summary>
    /// Builds a human-readable snippet from stderr lines collected during startup.
    /// Returns empty string if no stderr lines collected.
    /// </summary>
    private static string BuildStderrContext(List<string> stderrLines)
    {
        if (stderrLines.Count == 0) return "";

        // Get last non-empty lines (most relevant = closest to error)
        var relevant = stderrLines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Reverse()
            .Take(5)
            .Reverse()
            .ToList();

        if (relevant.Count == 0) return "";
        return $"\n  Stderr: {string.Join("\n  ", relevant)}";
    }

    // ── Cleanup ─────────────────────────────────────────────────────

    private void OnProcessExited(object? sender, EventArgs e)
    {
        OnFatalError?.Invoke(new InvalidOperationException("Coqui TTS (uv) process exited unexpectedly."));
    }

    private void CleanupProcess()
    {
        if (_process != null)
        {
            _process.Exited -= OnProcessExited;
            if (!_process.HasExited)
            {
                try { _process.Kill(true); } catch { }
            }
            _process.Dispose();
            _process = null;
        }
    }
}

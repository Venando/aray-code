using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenClawPTT.Services;

namespace OpenClawPTT.TTS.Providers;

/// <summary>
/// Coqui TTS provider using <c>uv</c> + a long-running <c>tts_service.py</c> subprocess.
///
/// <para>
/// This is the <c>uv</c>-backed replacement for the legacy <see cref="PythonTtsProvider"/>.
/// Uses the same JSON stdin/stdout protocol but <c>uv</c> handles Python, packages,
/// and dependencies automatically. No PythonPath config required.
/// </para>
/// </summary>
public sealed class CoquiUvTtsProvider : ITextToSpeech, IAsyncDisposable
{
    private readonly IColorConsole _console;
    private readonly CoquiUvEnvironment _environment;
    private readonly bool _debugLog;
    private readonly TimeSpan _synthesisTimeout;
    private readonly TimeSpan _writeTimeout;
    private readonly TimeSpan _startupTimeout;

    private Process? _process;
    private readonly SemaphoreSlim _sem = new(1, 1);
    private ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new();
    private CancellationTokenSource? _readCts;
    private bool _disposed;
    private bool _startupFailed;

    private int _consecutiveRestarts;
    private const int MaxConsecutiveRestarts = 3;
    private static readonly TimeSpan MinRestartDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRestartDelay = TimeSpan.FromSeconds(32);

    public string ProviderName => "Coqui TTS (uv)";

    public IReadOnlyList<string> AvailableVoices { get; } = new[] { "default" };

    public IReadOnlyList<string> AvailableModels { get; } = Array.Empty<string>();

    public CoquiUvTtsProvider(
        IColorConsole console,
        string? dataDir,
        string modelName,
        string? modelPath,
        string? ttsConfigPath,
        string? espeakNgPath = null,
        bool debugLog = false,
        TimeSpan? requestTimeout = null,
        TimeSpan? startupTimeout = null)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _environment = new CoquiUvEnvironment(dataDir, modelName, modelPath, ttsConfigPath, espeakNgPath);
        _debugLog = debugLog;
        _synthesisTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        _writeTimeout = TimeSpan.FromSeconds(5);
        _startupTimeout = startupTimeout ?? TimeSpan.FromSeconds(120);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _console.Log("coqui_uv_tts", "Loading Coqui TTS model via uv...");
        await _sem.WaitAsync(ct);
        try { await EnsureRunningAsync(ct); }
        finally { _sem.Release(); }
    }

    public async Task<byte[]> SynthesizeAsync(string text, string? voice = null, string? model = null, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _sem.WaitAsync(ct);
        try
        {
            await EnsureRunningAsync(ct);

            var id = Guid.NewGuid().ToString("N");
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;

            try
            {
                var request = new { text, id, voice, model };
                var line = JsonSerializer.Serialize(request) + "\n";

                var flushTask = Task.Run(async () =>
                {
                    await _process!.StandardInput.WriteAsync(line);
                    await _process.StandardInput.FlushAsync();
                }, ct);

                var timeoutTask = Task.Delay(_writeTimeout, ct);
                var winner = await Task.WhenAny(flushTask, timeoutTask);
                if (winner != flushTask)
                {
                    _process!.Kill(true);
                    _pending.TryRemove(id, out _);
                    throw new TimeoutException("Coqui TTS (uv) stdin write timed out.");
                }
                await flushTask;
            }
            catch
            {
                _pending.TryRemove(id, out _);
                throw;
            }

            string audioPath;
            try { audioPath = await tcs.Task.WaitAsync(_synthesisTimeout, ct); }
            catch (TaskCanceledException) { _pending.TryRemove(id, out _); throw; }
            catch (InvalidOperationException) { _pending.TryRemove(id, out _); throw; }

            if (!File.Exists(audioPath))
                throw new InvalidOperationException($"Coqui TTS (uv) did not produce output: {audioPath}");

            var audio = await File.ReadAllBytesAsync(audioPath, ct);
            try { File.Delete(audioPath); } catch { /* ignore */ }
            return audio;
        }
        finally { _sem.Release(); }
    }

    // ── Process lifecycle ───────────────────────────────────────────

    private async Task EnsureRunningAsync(CancellationToken ct)
    {
        if (_process != null && !_process.HasExited) return;
        if (_startupFailed)
            throw new InvalidOperationException("Coqui TTS (uv) provider is not available (previous startup failure).");

        // Verify uv is available
        if (!CoquiUvEnvironment.IsUvAvailable())
            throw new InvalidOperationException(
                $"uv is not installed. Install it with: {CoquiUvEnvironment.GetInstallInstructions()}");

        // Short-circuit if environment is known broken — don't retry
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

            if (_process != null) { _process.Dispose(); _process = null; }
            _readCts?.Cancel();
            _readCts?.Dispose();
            _readCts = new CancellationTokenSource();

            var psi = _environment.CreateProcessStartInfo();
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
                        _console.PrintWarning(
                            $"Coqui TTS (uv) startup failed: {errMsg}. " +
                            $"Attempt {_consecutiveRestarts}/{MaxConsecutiveRestarts}.");
                        break;
                    }
                }
            }

            if (readyLine != null && TryParseType(readyLine, out var rt) && rt == "ready" && _process != null)
            {
                _consecutiveRestarts = 0;
                _ = Task.Run(() => ReadLoopAsync(_process.StandardOutput, DispatchProtocol, _readCts.Token), _readCts.Token);
                _ = Task.Run(() => ReadLoopAsync(_process.StandardError, DispatchLog, _readCts.Token), _readCts.Token);
                return;
            }

            if (_process != null) { _process.Kill(true); _process.Dispose(); _process = null; }
            if (!gotErrorBeforeReady)
            {
                _consecutiveRestarts++;
                _console.PrintWarning(
                    $"Coqui TTS (uv) failed to start (expected 'ready', got: {(readyLine ?? "(null)")[..Math.Min(readyLine?.Length ?? 4, 80)]}). " +
                    $"Attempt {_consecutiveRestarts}/{MaxConsecutiveRestarts}.");
            }
        }

        _startupFailed = true;
        _consecutiveRestarts = 0;
        CoquiUvEnvironment.MarkUvBuildBroken(
            $"Coqui TTS (uv) failed to start after {MaxConsecutiveRestarts} attempts");
        throw new InvalidOperationException($"Coqui TTS (uv) failed to start after {MaxConsecutiveRestarts} attempts.");
    }

    // ── Line IO ─────────────────────────────────────────────────────

    private static async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken ct)
    {
        try
        {
            return await reader.ReadLineAsync(ct);
        }
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
        catch (Exception ex) { FailPendingRequests(new InvalidOperationException($"Read loop crashed: {ex.Message}")); }
    }

    // ── Protocol dispatch ───────────────────────────────────────────

    private void DispatchProtocol(string line)
    {
        if (!TryParseType(line, out var msgType)) return;

        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        switch (msgType)
        {
            case "ok":
                var okId = root.GetProperty("id").GetString();
                if (!string.IsNullOrEmpty(okId) && _pending.TryRemove(okId, out var okTcs))
                    okTcs.TrySetResult(root.GetProperty("path").GetString() ?? "");
                break;
            case "error":
                var errId = root.TryGetProperty("id", out var eid) ? eid.GetString() : null;
                var errMsg = root.TryGetProperty("msg", out var em) ? em.GetString() : "unknown";
                if (!string.IsNullOrEmpty(errId) && _pending.TryRemove(errId, out var errTcs))
                    errTcs.TrySetException(new InvalidOperationException($"Coqui TTS (uv) request '{errId}' failed: {errMsg ?? "unknown"}"));
                break;
        }
    }

    private void DispatchLog(string line)
    {
        if (!TryParseType(line, out _)) return;

        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        if (root.TryGetProperty("perf", out _))
        {
            var secs = root.GetProperty("time").GetDouble();
            var bytes = root.GetProperty("bytes").GetInt64();
            _console.Log("coqui_uv_tts", $"time: {secs:F2}s, {bytes / 1024.0:F1}KB");
        }
        else if (_debugLog)
        {
            var msg = root.TryGetProperty("msg", out var m) ? m.GetString() : line;
            if (msg != null) Console.Error.WriteLine(msg);
        }
    }

    // ── JSON helpers ────────────────────────────────────────────────

    private static bool TryParseType(string line, out string type)
    {
        type = "";
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("type", out var t))
            {
                type = t.GetString() ?? "";
                return true;
            }
        }
        catch { }
        return false;
    }

    private static string? TryExtractField(string line, string field)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.TryGetProperty(field, out var v) ? v.GetString() : null;
        }
        catch { return null; }
    }

    // ── Cleanup ─────────────────────────────────────────────────────

    private void OnProcessExited(object? sender, EventArgs e)
    {
        FailPendingRequests(new InvalidOperationException("Coqui TTS (uv) process exited unexpectedly."));
    }

    private void FailPendingRequests(Exception ex)
    {
        var pending = Interlocked.Exchange(ref _pending, new ConcurrentDictionary<string, TaskCompletionSource<string>>());
        foreach (var kvp in pending)
            kvp.Value.TrySetException(ex);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (!_pending.IsEmpty)
        {
            var ex = new ObjectDisposedException(nameof(CoquiUvTtsProvider));
            foreach (var kvp in _pending) kvp.Value.TrySetException(ex);
            _pending.Clear();
        }

        _readCts?.Cancel();

        try
        {
            if (_process != null && !_process.HasExited)
            {
                try
                {
                    var exitTask = Task.Run(async () =>
                    {
                        await _process.StandardInput.WriteAsync("EXIT\n");
                        await _process.StandardInput.FlushAsync();
                    });
                    using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    var winner = await Task.WhenAny(exitTask, Task.Delay(2, exitCts.Token));
                    if (winner != exitTask) { /* fall through to kill */ }
                    else await exitTask;
                }
                catch { }

                using var killCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await _process.WaitForExitAsync(killCts.Token); }
                catch (OperationCanceledException) { _process.Kill(true); }
            }
        }
        catch { }
        finally
        {
            _process?.Dispose();
            _process = null;
        }

        _readCts?.Dispose();
        _sem.Dispose();
    }
}

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenClawPTT.Services;

namespace OpenClawPTT.TTS.Providers;

/// <summary>
/// Python TTS provider using a long-running tts_service.py subprocess.
/// </summary>
public sealed class PythonTtsProvider : ITextToSpeech, IAsyncDisposable
{
    private readonly IColorConsole _console;
    private readonly string? _serviceScriptPathOverride;
    private readonly PythonEnvironment _environment;
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
    private bool _startupFailedReported;

    // Restart tracking: prevents infinite restart loops when the Python process or environment is broken.
    private int _consecutiveRestarts;
    private const int MaxConsecutiveRestarts = 3;

    // Exponential back-off delay between restart attempts (doubles each failure, capped at 32s).
    private static readonly TimeSpan MinRestartDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRestartDelay = TimeSpan.FromSeconds(32);

    public string ProviderName => "Python TTS";

    public IReadOnlyList<string> AvailableVoices { get; } = new[] { "default" };

    public IReadOnlyList<string> AvailableModels { get; } = Array.Empty<string>();

    public PythonTtsProvider(IColorConsole console, string? serviceScriptPathOverride, string pythonPath, string modelPath, string modelName, string? coquiConfigPath, string? espeakNgPath = null, bool debugLog = false, TimeSpan? requestTimeout = null, TimeSpan? startupTimeout = null)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _serviceScriptPathOverride = serviceScriptPathOverride;
        _environment = new PythonEnvironment(pythonPath, modelName, modelPath, coquiConfigPath, espeakNgPath);
        _debugLog = debugLog;
        _synthesisTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        _writeTimeout = TimeSpan.FromSeconds(5);
        _startupTimeout = startupTimeout ?? TimeSpan.FromSeconds(120);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _console.Log("python_tts_provider", "Loading TTS model...");

        await _sem.WaitAsync(ct);
        try
        {
            await EnsureRunningAsync(ct);
        }
        finally
        {
            _sem.Release();
        }
    }

    public async Task<byte[]> SynthesizeAsync(string text, string? voice = null, string? model = null, CancellationToken ct = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PythonTtsProvider));

        if (_startupFailed)
        {
            if (!_startupFailedReported)
            {
                _startupFailedReported = true;
                _console.PrintWarning("Python TTS is not available (startup failed). Audio will be skipped for this request.");
            }
            throw new InvalidOperationException("Python TTS provider is not available due to a previous startup failure.");
        }

        await _sem.WaitAsync(ct);
        try
        {
            var id = Guid.NewGuid().ToString("N");
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;

            try
            {
                var request = new { text, id, voice, model };
                var line = JsonSerializer.Serialize(request) + "\n";

                // Timeout protection for the write path. WriteAsync buffers data and
                // FlushAsync blocks when the OS pipe buffer is full. If Python is stuck
                // in inference or the buffer is saturated, we would otherwise hang forever.
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
                    throw new TimeoutException("Python TTS stdin write timed out.");
                }

                await flushTask; // surface any exception from the write/flush itself
            }
            catch
            {
                _pending.TryRemove(id, out _);
                throw;
            }

            string audioPath;
            try
            {
                audioPath = await tcs.Task.WaitAsync(_synthesisTimeout, ct);
            }
            catch (TaskCanceledException)
            {
                _pending.TryRemove(id, out _);
                throw;
            }
            catch (InvalidOperationException)
            {
                _pending.TryRemove(id, out _);
                throw;
            }

            if (!File.Exists(audioPath))
                throw new InvalidOperationException($"Python TTS did not produce output file: {audioPath}");

            var audio = await File.ReadAllBytesAsync(audioPath, ct);

            try { File.Delete(audioPath); } catch { /* ignore */ }

            return audio;
        }
        finally
        {
            _sem.Release();
        }
    }

    private async Task EnsureRunningAsync(CancellationToken ct)
    {
        if (_process != null && !_process.HasExited)
        {
            // Process is alive — but if we have been retrying, check the limit now.
            if (_consecutiveRestarts >= MaxConsecutiveRestarts)
            {
                _console.PrintWarning(
                    $"Python TTS failed to start after {MaxConsecutiveRestarts} attempts. Giving up.");
                _startupFailed = true;
                _consecutiveRestarts = 0;
                return;
            }
            return;
        }

        // Enforce restart limit on the restart path too.
        if (_consecutiveRestarts >= MaxConsecutiveRestarts)
        {
            _console.PrintWarning(
                $"Python TTS process died and has exceeded the restart limit ({MaxConsecutiveRestarts}). Giving up.");
            _startupFailed = true;
            _consecutiveRestarts = 0;
            return;
        }

        // Kill any zombie process from the previous attempt.
        if (_process != null)
        {
            _process.Dispose();
            _process = null;
        }

        _readCts?.Cancel();
        _readCts?.Dispose();
        _readCts = new CancellationTokenSource();

        // Exponential back-off: delay before the next restart attempt.
        if (_consecutiveRestarts > 0)
        {
            var delay = TimeSpan.FromTicks(Math.Min(
                MinRestartDelay.Ticks << (_consecutiveRestarts - 1),
                MaxRestartDelay.Ticks));
            await Task.Delay(delay, ct);
        }

        ProcessStartInfo psi = _environment.CreateProcessStartInfo(_serviceScriptPathOverride);

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.Exited += OnProcessExited;

        _process.Start();

        if (_process.HasExited)
        {
            var exitCode = _process.ExitCode;
            _process.Dispose();
            _process = null;
            _consecutiveRestarts++;

            if (_consecutiveRestarts >= MaxConsecutiveRestarts)
            {
                _console.PrintWarning($"Python TTS process exited immediately (code: {exitCode}) after {MaxConsecutiveRestarts} attempts. Giving up.");
                _startupFailed = true;
                _consecutiveRestarts = 0;
                return;
            }

            throw new InvalidOperationException($"Python process exited immediately with code: {exitCode}");
        }

        // Wait for READY — loop until we find {"type":"ready"} or plain "READY", logging all lines
        using var timeoutCts = new CancellationTokenSource(_startupTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        string? readyLine = null;
        while (!linked.Token.IsCancellationRequested)
        {
            readyLine = await ReadLineAsync(_process.StandardOutput, linked.Token);
            
            if (readyLine == null)
                break;

            if (IsReadyLine(readyLine))
                break;
                
            // Detect startup errors before READY to avoid hanging on model load failures
            if (JsonHelper.TryParseJson(readyLine ?? "", out var jsonDoc, out var msgType) &&
                msgType == MessageType.Error)
            {
                var errMsg = jsonDoc.RootElement.GetProperty("msg").GetString();
                jsonDoc.Dispose();
                _process.Kill(true);
                _process.Dispose();
                _process = null;
                _consecutiveRestarts++;

                if (_consecutiveRestarts >= MaxConsecutiveRestarts)
                {
                    _console.PrintWarning($"Python TTS startup permanently failed (error: {errMsg}). Giving up after {MaxConsecutiveRestarts} attempts.");
                    _startupFailed = true;
                    _consecutiveRestarts = 0;
                    return;
                }

                throw new InvalidOperationException($"Python TTS startup failed: {errMsg}");
            }
        }

        if (readyLine == null || !IsReadyLine(readyLine))
        {
            _process.Kill(true);
            _process.Dispose();
            _process = null;
            _consecutiveRestarts++;

            if (_consecutiveRestarts >= MaxConsecutiveRestarts)
            {
                _console.PrintWarning($"Python TTS failed to start (expected READY, got: {readyLine ?? "(null)"}). Giving up after {MaxConsecutiveRestarts} attempts.");
                _startupFailed = true;
                _consecutiveRestarts = 0;
                return;
            }

            throw new InvalidOperationException($"Python TTS failed to start. Expected READY, got: {readyLine}");
        }

        // Successfully reached READY — reset restart counter.
        _consecutiveRestarts = 0;

        _console.Log("python_tts_provider", "Ready");

        // Stderr: structured log messages (perf, warn, info, error)
        // Stdout: structured protocol messages (ready, done, ok)
        _ = Task.Run(() => ReadLoopAsync(_process.StandardError, DispatchLog, _readCts.Token), _readCts.Token);
        _ = Task.Run(() => ReadLoopAsync(_process.StandardOutput, DispatchProtocol, _readCts.Token), _readCts.Token);
    }

    private async Task<string?> ReadLineAsync(StreamReader reader, CancellationToken ct)
    {
        try
        {
            // Poll HasExited to avoid blocking indefinitely if the process dies between the
            // loop check in ReadLoopAsync and the actual read.
            // Use Peek() instead of EndOfStream to avoid blocking in async context (CA2024)
            while (_process is { HasExited: false })
            {
                if (ct.IsCancellationRequested)
                    return null;
                var peekResult = reader.Peek();
                if (peekResult == -1)
                    return null; // End of stream
                if (peekResult != -1)
                    return await reader.ReadLineAsync(ct);
                await Task.Delay(500, ct);
            }
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private async Task ReadLoopAsync(StreamReader reader, Action<string> onLine, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _process is { HasExited: false })
            {
                var line = await ReadLineAsync(reader, ct);
                if (string.IsNullOrWhiteSpace(line))
                    break;
                onLine(line);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            FailPendingRequests(new InvalidOperationException(
                $"Python TTS read loop crashed: {ex.Message}"));
        }
    }

    private void DispatchProtocol(string line)
    {
        if (!JsonHelper.TryParseJson(line, out JsonDocument? jsonDocument, out string? msgType))
        {
            return;
        }

        using (jsonDocument)
        {
            var root = jsonDocument.RootElement;
            switch (msgType)
            {
                case MessageType.Ok:
                    var okId = root.GetProperty("id").GetString();
                    if (!string.IsNullOrEmpty(okId) && _pending.TryRemove(okId, out var okTcs))
                    {
                        var path = root.GetProperty("path").GetString();
                        okTcs.TrySetResult(path ?? string.Empty);
                    }
                    break;
                case MessageType.Error:
                    {
                        var errId = root.TryGetProperty("id", out var eid) ? eid.GetString() : null;
                        var errMsg = root.TryGetProperty("msg", out var em) ? em.GetString() : "unknown";
                        if (!string.IsNullOrEmpty(errId) && _pending.TryRemove(errId, out var errTcs))
                            errTcs.TrySetException(new InvalidOperationException(
                                $"Python TTS request '{errId}' failed: {errMsg ?? "unknown"}"));
                        break;
                    }
                default:
                    if (_debugLog) Console.Error.WriteLine(line);
                    break;
            }
        }
    }

    private static bool IsReadyLine(string line)
    {
        if (JsonHelper.TryParseJson(line, out var jsonDoc, out var msgType))
        {
            jsonDoc.Dispose();
            return msgType == MessageType.Ready;
        }
        return false;
    }

    private void DispatchLog(string line)
    {
        if (!JsonHelper.TryParseJson(line, out JsonDocument? jsonDocument, out string? msgType))
        {
            return;
        }

        using (jsonDocument)
        {
            var root = jsonDocument.RootElement;
            switch (msgType)
            {
                case MessageType.Performance:
                    var secs = root.GetProperty("time").GetDouble();
                    var bytes = root.GetProperty("bytes").GetInt64();
                    _console.Log("audio processing:", $"time: {secs:F2}s, {bytes / 1024.0:F1}KB");
                    break;
                case MessageType.Warn:
                    var msg = root.GetProperty("msg").GetString();
                    _console.PrintWarning($"TTS Performance Alert: {msg}");
                    break;
                case MessageType.Info:
                case MessageType.Debug:
                    if (_debugLog)
                    {
                        var infoMsg = root.TryGetProperty("msg", out var m) ? m.GetString() : line;
                        Console.Error.WriteLine(infoMsg);
                    }
                    break;
                case MessageType.Error:
                    var errMsg = root.TryGetProperty("msg", out var e) ? e.GetString() : line;
                    _console.PrintError(errMsg ?? line);
                    break;
                default:
                    if (_debugLog) Console.Error.WriteLine(line);
                    break;
            }
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        FailPendingRequests(new InvalidOperationException("Python TTS process exited unexpectedly."));
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

        // Fail all pending requests before cancelling the read loop.
        // This ensures in-flight SynthesizeAsync calls complete with ObjectDisposedException
        // rather than hanging forever.
        if (!_pending.IsEmpty)
        {
            var ex = new ObjectDisposedException(nameof(PythonTtsProvider));
            foreach (var kvp in _pending)
                kvp.Value.TrySetException(ex);
            _pending.Clear();
        }

        _readCts?.Cancel();

        try
        {
            if (_process != null && !_process.HasExited)
            {
                try
                {
                    // Same timeout protection as the synthesis write path. If the EXIT
                    // signal can't be written, fall through to Kill() — which is fine
                    // since the process is shutting down anyway.
                    var exitTask = Task.Run(async () =>
                    {
                        await _process.StandardInput.WriteAsync("EXIT\n");
                        await _process.StandardInput.FlushAsync();
                    });

                    using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    var timeoutTask = Task.Delay(2, exitCts.Token);
                    var winner = await Task.WhenAny(exitTask, timeoutTask);
                    if (winner != exitTask) { /* ignore — Kill() follows */ }
                    else await exitTask;
                }
                catch { /* ignore */ }

                using var killCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await _process.WaitForExitAsync(killCts.Token);
                }
                catch (OperationCanceledException)
                {
                    _process.Kill(true);
                }
            }
        }
        catch { /* ignore */ }
        finally
        {
            _process?.Dispose();
            _process = null;
        }

        _readCts?.Dispose();
        _sem.Dispose();
    }
}

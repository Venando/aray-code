using System.Collections.Concurrent;
using System.Text.Json;
using ArayCode.Services;

namespace ArayCode.TTS.Providers;

/// <summary>
/// Coqui TTS provider using <c>uv</c> + a long-running <c>tts_service.py</c> subprocess.
///
/// <para>
/// Uses the same JSON stdin/stdout protocol as the legacy Python provider but
/// <c>uv</c> handles Python, packages, and dependencies automatically.
/// Process lifecycle is delegated to <see cref="CoquiUvProcessRunner"/> (SRP).
/// </para>
/// </summary>
public sealed class CoquiUvTtsProvider : ITextToSpeech, IAsyncDisposable
{
    private readonly IColorConsole _console;
    private readonly CoquiUvEnvironment _environment;
    private readonly CoquiUvProcessRunner _processRunner;
    private readonly bool _debugLog;
    private readonly TimeSpan _synthesisTimeout;
    private readonly TimeSpan _writeTimeout;

    private readonly SemaphoreSlim _sem = new(1, 1);
    private ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new();
    private bool _disposed;

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
        _processRunner = new CoquiUvProcessRunner(
            _environment, _console, _pending,
            onProtocolLine: DispatchProtocol,
            onLogLine: _debugLog ? DispatchLog : null,
            startupTimeout: startupTimeout);
        _debugLog = debugLog;
        _synthesisTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        _writeTimeout = TimeSpan.FromSeconds(5);

        // Wire up fatal error handling
        _processRunner.OnFatalError += OnProcessFatalError;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _console.Log("coqui_uv_tts", "Loading Coqui TTS model via uv...");
        await _sem.WaitAsync(ct);
        try
        {
            await _processRunner.EnsureRunningAsync(ct);
        }
        finally { _sem.Release(); }
    }

    public async Task<byte[]> SynthesizeAsync(string text, string? voice = null, string? model = null, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _sem.WaitAsync(ct);
        try
        {
            await _processRunner.EnsureRunningAsync(ct);

            var id = Guid.NewGuid().ToString("N");
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;

            try
            {
                var request = new { text, id, voice, model };
                var line = JsonSerializer.Serialize(request) + "\n";

                var flushTask = Task.Run(async () =>
                {
                    await _processRunner.WriteRequestAsync(line, ct);
                }, ct);

                var timeoutTask = Task.Delay(_writeTimeout, ct);
                var winner = await Task.WhenAny(flushTask, timeoutTask);
                if (winner != flushTask)
                {
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

    // ── Protocol dispatch ───────────────────────────────────────────

    private void DispatchProtocol(string line)
    {
        if (!CoquiUvProcessRunner.TryParseType(line, out var msgType)) return;

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
        if (!CoquiUvProcessRunner.TryParseType(line, out _)) return;

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

    // ── Error handling ──────────────────────────────────────────────

    private void OnProcessFatalError(Exception ex)
    {
        FailPendingRequests(ex);
    }

    private void FailPendingRequests(Exception ex)
    {
        var pending = Interlocked.Exchange(ref _pending, new ConcurrentDictionary<string, TaskCompletionSource<string>>());
        foreach (var kvp in pending)
            kvp.Value.TrySetException(ex);
    }

    // ── Cleanup ─────────────────────────────────────────────────────

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

        await _processRunner.StopAsync();
        _processRunner.Dispose();
        _sem.Dispose();
    }
}

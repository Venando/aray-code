using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;

namespace OpenClawPTT.TTS.Providers.Supertonic;

/// <summary>
/// Supertonic 3 TTS provider — fast local TTS via uv-managed Python subprocess.
///
/// <para>
/// Uses the same JSON stdin/stdout protocol pattern as <c>CoquiUvTtsProvider</c>.
/// The Python subprocess runs <c>supertonic_service.py</c> via <c>uv run</c>,
/// which auto-downloads the ~99M model on first use.
/// </para>
/// </summary>
public sealed class SupertonicTtsProvider : ITextToSpeech, IAsyncDisposable
{
    private readonly IColorConsole _console;
    private readonly SupertonicProcessRunner _processRunner;
    private readonly string _defaultVoice;
    private readonly string _defaultLang;
    private readonly int _defaultQuality;
    private readonly double _defaultSpeed;
    private readonly TimeSpan _synthesisTimeout;
    private readonly TimeSpan _writeTimeout;

    private readonly SemaphoreSlim _sem = new(1, 1);
    private ConcurrentDictionary<string, TaskCompletionSource<string>> _pending = new();
    private bool _disposed;

    public string ProviderName => "Supertonic 3 TTS";

    /// <summary>10 preset voices — 5 male (M1–M5) and 5 female (F1–F5).</summary>
    public IReadOnlyList<string> AvailableVoices { get; } =
    [
        "M1", "M2", "M3", "M4", "M5",
        "F1", "F2", "F3", "F4", "F5",
    ];

    public IReadOnlyList<string> AvailableModels { get; } = ["supertonic-3"];

    /// <summary>
    /// Creates a new Supertonic 3 TTS provider.
    /// </summary>
    /// <param name="console">Console for logging.</param>
    /// <param name="dataDir">Data directory for the uv project (stores pyproject.toml and service script).</param>
    /// <param name="defaultVoice">Default voice name (M1–M5, F1–F5).</param>
    /// <param name="defaultLang">Default language code (e.g. "en").</param>
    /// <param name="defaultQuality">Quality level 5–12 (default 8).</param>
    /// <param name="defaultSpeed">Speed multiplier 0.7–2.0 (default 1.05).</param>
    /// <param name="requestTimeout">Per-request timeout.</param>
    /// <param name="startupTimeout">Startup timeout.</param>
    public SupertonicTtsProvider(
        IColorConsole console,
        string? dataDir = null,
        string defaultVoice = "M1",
        string defaultLang = "en",
        int defaultQuality = 8,
        double defaultSpeed = 1.05,
        TimeSpan? requestTimeout = null,
        TimeSpan? startupTimeout = null)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _defaultVoice = defaultVoice;
        _defaultLang = defaultLang;
        _defaultQuality = defaultQuality;
        _defaultSpeed = defaultSpeed;
        _synthesisTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        _writeTimeout = TimeSpan.FromSeconds(5);

        var projectDir = Path.Combine(
            dataDir ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".openclaw-ptt"),
            "supertonic-tts-env");

        _processRunner = new SupertonicProcessRunner(
            projectDir, _console, _pending,
            onProtocolLine: DispatchProtocol,
            startupTimeout: startupTimeout);

        _processRunner.OnFatalError += OnProcessFatalError;
    }

    /// <summary>
    /// Initializes the provider by starting the uv subprocess.
    /// Called by <see cref="TtsService"/> constructor.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _console.Log("supertonic_tts", "Loading Supertonic 3 model via uv...");
        await _sem.WaitAsync(ct);
        try
        {
            await _processRunner.EnsureRunningAsync(ct);
            _console.LogOk("supertonic_tts", "Supertonic 3 ready.");
        }
        finally { _sem.Release(); }
    }

    /// <summary>
    /// Synthesizes text to audio (WAV) bytes.
    /// </summary>
    /// <param name="text">Text to synthesize.</param>
    /// <param name="voice">Voice name (M1–M5, F1–F5).</param>
    /// <param name="model">Ignored — Supertonic only has one model.</param>
    /// <param name="ct">Cancellation token.</param>
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
                var request = new
                {
                    id,
                    text,
                    voice = voice ?? _defaultVoice,
                    lang = _defaultLang,
                    quality = _defaultQuality,
                    speed = _defaultSpeed,
                };
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
                    throw new TimeoutException("Supertonic TTS stdin write timed out.");
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
                throw new InvalidOperationException($"Supertonic TTS did not produce output: {audioPath}");

            var audio = await File.ReadAllBytesAsync(audioPath, ct);
            try { File.Delete(audioPath); } catch { /* best-effort cleanup */ }
            return audio;
        }
        finally { _sem.Release(); }
    }

    // ── Protocol dispatch ───────────────────────────────────────────

    private void DispatchProtocol(string line)
    {
        if (!SupertonicProcessRunner.TryParseType(line, out var msgType)) return;

        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        switch (msgType)
        {
            case "ok":
                var okId = root.GetProperty("id").GetString();
                if (!string.IsNullOrEmpty(okId) && _pending.TryRemove(okId, out var okTcs))
                {
                    var path = root.TryGetProperty("path", out var p) ? p.GetString() : null;
                    okTcs.TrySetResult(path ?? "");
                }
                break;

            case "error":
                var errId = root.TryGetProperty("id", out var eid) ? eid.GetString() : null;
                var errMsg = root.TryGetProperty("msg", out var em) ? em.GetString() : "unknown";
                if (!string.IsNullOrEmpty(errId) && _pending.TryRemove(errId, out var errTcs))
                    errTcs.TrySetException(new InvalidOperationException($"Supertonic TTS request '{errId}' failed: {errMsg ?? "unknown"}"));
                break;
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
            var ex = new ObjectDisposedException(nameof(SupertonicTtsProvider));
            foreach (var kvp in _pending) kvp.Value.TrySetException(ex);
            _pending.Clear();
        }

        await _processRunner.StopAsync();
        _processRunner.Dispose();
        _sem.Dispose();
    }
}

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;

namespace OpenClawPTT.TTS.Providers.Supertonic;

/// <summary>
/// Manages the long-running <c>uv run python supertonic_service.py</c>
/// subprocess lifecycle. Simplified compared to <c>CoquiUvProcessRunner</c>
/// — no CUDA fallback, no model path env vars, single Python requirement.
/// </summary>
internal sealed class SupertonicProcessRunner : IDisposable
{
    private readonly string _projectDir;
    private readonly IColorConsole _console;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pending;
    private readonly Action<string>? _onProtocolLine;
    private readonly TimeSpan _startupTimeout;

    private Process? _process;
    private CancellationTokenSource? _readCts;
    private bool _disposed;
    private int _consecutiveRestarts;
    private bool _startupFailed;

    private const int MaxConsecutiveRestarts = 3;
    private static readonly TimeSpan MinRestartDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRestartDelay = TimeSpan.FromSeconds(16);

    public bool IsRunning => _process is { HasExited: false };

    public event Action<Exception>? OnFatalError;

    public SupertonicProcessRunner(
        string projectDir,
        IColorConsole console,
        ConcurrentDictionary<string, TaskCompletionSource<string>> pending,
        Action<string>? onProtocolLine = null,
        TimeSpan? startupTimeout = null)
    {
        _projectDir = projectDir ?? throw new ArgumentNullException(nameof(projectDir));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _pending = pending ?? throw new ArgumentNullException(nameof(pending));
        _onProtocolLine = onProtocolLine;
        _startupTimeout = startupTimeout ?? TimeSpan.FromSeconds(60);
    }

    /// <summary>
    /// Ensures the uv subprocess is running. Starts it if not already running,
    /// retrying with exponential backoff on failure.
    /// </summary>
    public async Task EnsureRunningAsync(CancellationToken ct)
    {
        if (IsRunning) return;
        if (_startupFailed)
            throw new InvalidOperationException("Supertonic 3 TTS is not available (previous startup failure).");

        // Verify uv is available
        var uvPath = ResolveUvPath();
        if (uvPath == null)
            throw new InvalidOperationException(
                "uv is not installed. Install it with: " +
                (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "powershell -c \"irm https://astral.sh/uv/install.ps1 | iex\""
                    : "curl -LsSf https://astral.sh/uv/install.sh | sh"));

        EnsureProjectFiles();

        while (_consecutiveRestarts < MaxConsecutiveRestarts)
        {
            if (_consecutiveRestarts > 0)
            {
                var delay = TimeSpan.FromTicks(Math.Min(
                    MinRestartDelay.Ticks << (_consecutiveRestarts - 1), MaxRestartDelay.Ticks));
                _console.Log("supertonic_tts", $"Waiting {delay.TotalSeconds}s before retry #{_consecutiveRestarts + 1}...");
                await Task.Delay(delay, ct);
            }

            CleanupProcess();
            _readCts?.Cancel();
            _readCts?.Dispose();
            _readCts = new CancellationTokenSource();

            var psi = BuildProcessStartInfo(uvPath);
            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.Exited += OnProcessExited;

            _console.Log("supertonic_tts", $"Starting: {psi.FileName} {psi.Arguments}");
            _process.Start();
            _console.Log("supertonic_tts", $"Started (PID: {_process.Id}).");

            if (_process.HasExited)
            {
                _process.Dispose();
                _process = null;
                _consecutiveRestarts++;
                _console.PrintWarning(
                    $"Supertonic 3 exited immediately (code: {_process?.ExitCode}). " +
                    $"Attempt {_consecutiveRestarts}/{MaxConsecutiveRestarts}.");
                continue;
            }

            // Wait for {"type":"ready"} with timeout
            using var timeoutCts = new CancellationTokenSource(_startupTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            string? readyLine = null;

            while (!linked.Token.IsCancellationRequested)
            {
                readyLine = await ReadLineAsync(_process.StandardOutput, linked.Token);
                if (readyLine == null) break;

                if (TryParseType(readyLine, out var msgType))
                {
                    if (msgType == "ready") break;
                    if (msgType == "error")
                    {
                        var errMsg = TryExtractField(readyLine, "msg") ?? "unknown startup error";
                        _process.Kill(entireProcessTree: true);
                        _process.Dispose();
                        _process = null;
                        _consecutiveRestarts++;
                        _console.PrintWarning(
                            $"Supertonic 3 startup failed: {errMsg}. " +
                            $"Attempt {_consecutiveRestarts}/{MaxConsecutiveRestarts}.");
                        break;
                    }
                }
            }

            if (readyLine != null && TryParseType(readyLine, out var rt) && rt == "ready" && _process != null)
            {
                _consecutiveRestarts = 0;
                StartReadLoop();
                return;
            }

            if (_process != null) { _process.Kill(entireProcessTree: true); _process.Dispose(); _process = null; }
            _consecutiveRestarts++;
            _console.PrintWarning(
                $"Supertonic 3 failed to start (expected 'ready', got: {(readyLine ?? "(null)")[..Math.Min(readyLine?.Length ?? 4, 80)]}). " +
                $"Attempt {_consecutiveRestarts}/{MaxConsecutiveRestarts}.");
        }

        _startupFailed = true;
        _consecutiveRestarts = 0;
        throw new InvalidOperationException($"Supertonic 3 failed to start after {MaxConsecutiveRestarts} attempts.");
    }

    /// <summary>Starts the stdout read loop for protocol dispatch.</summary>
    private void StartReadLoop()
    {
        if (_readCts == null || _process == null || _onProtocolLine == null) return;
        var ct = _readCts.Token;
        _ = Task.Run(() => ReadLoopAsync(_process.StandardOutput, _onProtocolLine, ct), ct);
    }

    /// <summary>Writes a JSON line to the subprocess stdin.</summary>
    public async Task WriteRequestAsync(string jsonLine, CancellationToken ct)
    {
        var process = _process ?? throw new InvalidOperationException("Process not running.");
        await process.StandardInput.WriteAsync(jsonLine.AsMemory(), ct);
        await process.StandardInput.FlushAsync();
    }

    /// <summary>Gracefully stops the process, killing if it doesn't stop.</summary>
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
            await Task.WhenAny(exitTask, Task.Delay(grace));
        }
        catch { }

        try
        {
            using var killCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try { await _process.WaitForExitAsync(killCts.Token); }
            catch (OperationCanceledException) { _process.Kill(entireProcessTree: true); }
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

    // ── Process creation ────────────────────────────────────────────

    private ProcessStartInfo BuildProcessStartInfo(string uvPath)
    {
        return new ProcessStartInfo
        {
            FileName = uvPath,
            Arguments = $"run --directory \"{_projectDir}\" python \"{Path.Combine(_projectDir, "supertonic_service.py")}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
    }

    // ── Project files ───────────────────────────────────────────────

    private void EnsureProjectFiles()
    {
        Directory.CreateDirectory(_projectDir);

        var pyprojectPath = Path.Combine(_projectDir, "pyproject.toml");
        File.WriteAllText(pyprojectPath, SupertonicScripts.PyProjectToml, Encoding.UTF8);

        var scriptPath = Path.Combine(_projectDir, "supertonic_service.py");
        File.WriteAllText(scriptPath, SupertonicScripts.ServiceScript, Encoding.UTF8);
    }

    // ── uv discovery ────────────────────────────────────────────────

    private static string? ResolveUvPath()
    {
        var name = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "uv.exe" : "uv";
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";

        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            var fp = Path.Combine(dir, name);
            if (File.Exists(fp)) return fp;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var common = new[]
        {
            Path.Combine(home, ".local", "bin", name),
            Path.Combine(home, ".cargo", "bin", name),
            $"/usr/local/bin/{name}",
        };
        foreach (var p in common)
        {
            if (File.Exists(p)) return p;
        }
        return null;
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

    internal static string? TryExtractField(string line, string field)
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
        OnFatalError?.Invoke(new InvalidOperationException("Supertonic 3 process exited unexpectedly."));
    }

    private void CleanupProcess()
    {
        if (_process != null)
        {
            _process.Exited -= OnProcessExited;
            if (!_process.HasExited)
            {
                try { _process.Kill(entireProcessTree: true); } catch { }
            }
            _process.Dispose();
            _process = null;
        }
    }
}

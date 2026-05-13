using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;

namespace OpenClawPTT.TTS.Providers;

/// <summary>
/// Manages Coqui TTS models via <c>uv</c> — listing, pre-downloading, and deleting.
/// Models are fetched live from the <c>TTS</c> package (<c>uv run python -c "..."</c>)
/// with a hardcoded fallback for offline scenarios.
/// </summary>
public sealed class CoquiTtsModelManager
{
    private readonly string _projectDir;
    private readonly IStreamShellHost _host;
    private readonly TimeSpan _downloadTimeout = TimeSpan.FromMinutes(30);

    // Cached live model list (null = not fetched yet, empty = fetch failed)
    private static IReadOnlyList<CoquiTtsModelInfo>? s_liveModels;
    private static readonly object s_liveLock = new();

    /// <summary>
    /// True when the last <see cref="GetAvailableModelsAsync"/> call returned
    /// the hardcoded fallback list rather than live models from the TTS package.
    /// </summary>
    public static bool IsUsingFallbackModels { get; private set; }

    /// <summary>
    /// Human-readable reason from the last fetch attempt (set even on success).
    /// Empty if the last call hasn't been made yet.
    /// </summary>
    public static string LastFetchErrorDetail { get; private set; } = string.Empty;

    /// <summary>
    /// Hardcoded fallback list — well-known Coqui TTS models.
    /// Used when uv is not available or the live fetch fails.
    /// </summary>
    public static readonly IReadOnlyList<CoquiTtsModelInfo> FallbackModels = new List<CoquiTtsModelInfo>
    {
        new("tts_models/multilingual/mxtts/vits",       "XTTS v2 — multilingual, voice cloning, ~1.9 GB"),
        new("tts_models/en/ljspeech/vits",              "LJSpeech VITS — English single-speaker, ~300 MB"),
        new("tts_models/en/ljspeech/tacotron2-DDC",     "Tacotron2 + WaveGlow — English, ~500 MB"),
        new("tts_models/en/ljspeech/fast_pitch",        "FastPitch — English, fast, ~300 MB"),
        new("tts_models/en/ljspeech/glow-tts",          "Glow-TTS — English, ~200 MB"),
        new("tts_models/en/vctk/vits",                  "VCTK VITS — English multi-speaker, ~400 MB"),
        new("tts_models/en/jenny/jenny",                "Jenny — English female, ~50 MB"),
        new("tts_models/en/sam/tacotron-DDC",           "SAM Tacotron — English male, ~500 MB"),
        new("tts_models/es/mai_speak/vits",             "Spanish VITS, ~300 MB"),
        new("tts_models/fr/css10/vits",                 "French VITS, ~300 MB"),
        new("tts_models/de/thorsten/vits",              "German VITS, ~300 MB"),
        new("tts_models/uk/mai_speak/vits",             "Ukrainian VITS, ~300 MB"),
        new("tts_models/ja/kokoro/vits",                "Japanese Kokoro VITS, ~300 MB"),
    };

    /// <summary>
    /// Returns the best available model list: live from <c>TTS</c> package if uv is
    /// available and fetch succeeds; otherwise the hardcoded fallback.
    /// </summary>
    public static async Task<IReadOnlyList<CoquiTtsModelInfo>> GetAvailableModelsAsync(
        IStreamShellHost host,
        string? dataDir = null,
        CancellationToken ct = default)
    {
        // Return cached live list if we have one
        lock (s_liveLock)
        {
            if (s_liveModels is { Count: > 0 })
                return s_liveModels;
        }

        if (!CoquiUvEnvironment.IsUvAvailable())
        {
            IsUsingFallbackModels = true;
            LastFetchErrorDetail = "uv is not installed";
            host.AddMessage("[yellow]    ⚠ uv not found — using built-in (offline) model list.[/]");
            host.AddMessage($"[grey]      Install: {CoquiUvEnvironment.GetInstallInstructions()}[/]");
            return FallbackModels;
        }

        try
        {
            var setupPanel = new CoquiEnvSetupPanel();
            host.SetBottomPanel(setupPanel);

            try
            {
                var liveList = await FetchFromUvAsync(host, dataDir,
                    progressCallback: (status, line, error) => setupPanel.SetStatus(status, line, error),
                    ct).ConfigureAwait(false);

                if (liveList is { Count: > 0 })
                {
                    IsUsingFallbackModels = false;
                    LastFetchErrorDetail = string.Empty;
                    setupPanel.SetCompleted(true, $"Found {liveList.Count} models");
                    lock (s_liveLock) { s_liveModels = liveList; }
                    host.AddMessage($"[green]    \u2713 Found {liveList.Count} models live from Coqui TTS.[/]");
                    return liveList;
                }

                IsUsingFallbackModels = true;
                LastFetchErrorDetail = "TTS package returned no models";
                setupPanel.SetCompleted(false, "No models returned");
            }
            finally
            {
                // Keep the panel visible briefly so the user sees completion
                await Task.Delay(500, CancellationToken.None);
                host.ResetBottomPanel();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            IsUsingFallbackModels = true;
            LastFetchErrorDetail = "User cancelled";
            host.ResetBottomPanel();
            throw; // User cancelled — propagate
        }
        catch (OperationCanceledException)
        {
            // uv command timed out (5 min) — fall back to built-in list
            IsUsingFallbackModels = true;
            LastFetchErrorDetail = "Live model fetch timed out after 5 minutes";
            host.AddMessage("[yellow]    \u26a0 Live model fetch timed out after 5 minutes, using built-in list.[/]");
        }
        catch (Exception ex)
        {
            IsUsingFallbackModels = true;
            // Show the full error detail — ex.Message contains stderr when thrown from FetchFromUvAsync
            var errorMsg = ex.Message;
            host.AddMessage("[red]    \u2717 Live model fetch failed. Error from uv:[/]");

            // ex.Message from InvalidOperationException includes: "uv exit=N: <stderr>"
            // Split into lines for readability; avoid flooding with too many lines
            var errorLines = errorMsg.Split('\n');
            var shown = 0;
            foreach (var line in errorLines)
            {
                if (shown >= 20) { host.AddMessage("[red]      ... (output truncated)[/]"); break; }
                var trimmedLine = line.Trim();
                if (!string.IsNullOrWhiteSpace(trimmedLine))
                {
                    host.AddMessage($"[red]      {trimmedLine}[/]");
                    shown++;
                }
            }

            LastFetchErrorDetail = errorMsg;
            host.AddMessage("[yellow]    \u26a0 Falling back to built-in (offline) model list.[/]");
            host.AddMessage("[grey]      Fix the uv/Python issues above and re-run setup to get live models.[/]");
        }

        return FallbackModels;
    }

    /// <summary>
    /// Fetches the full model list from the installed <c>TTS</c> package via <c>uv run python</c>.
    /// Runs <c>TTS().list_models()</c> which queries the HuggingFace <c>coqui/TTS</c> repo.
    /// First-run dependency resolution (Python + TTS + torch + pandas + ...) can take
    /// several minutes, so stderr is streamed via <paramref name="progressCallback"/>.
    /// </summary>
    private static async Task<IReadOnlyList<CoquiTtsModelInfo>?> FetchFromUvAsync(
        IStreamShellHost host,
        string? dataDir,
        Action<string, string, string?>? progressCallback,
        CancellationToken ct)
    {
        var projectDir = Path.Combine(
            dataDir ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".openclaw-ptt"),
            "coqui-tts-env");

        // Ensure pyproject.toml exists so uv can resolve
        var env = new CoquiUvEnvironment(dataDir, "tts_models/en/ljspeech/vits", null, null, null);
        env.EnsureProjectFiles();

        var uvPath = CoquiUvEnvironment.FindUv() ?? "uv";
        var cmd = "import json; from TTS.api import TTS; " +
                  "models = TTS().list_models(); " +
                  "print(json.dumps(models))";

        var psi = new ProcessStartInfo
        {
            FileName = uvPath,
            Arguments = $"run --directory \"{projectDir}\" python -c \"{cmd}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        host.AddMessage("[grey]    Fetching model list from coqui/TTS on HuggingFace...[/]");
        progressCallback?.Invoke("Resolving packages", "uv setting up Python environment...", null);

        // First-run dep resolution can take minutes (Python + TTS + torch + pandas + ...)
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start uv run for model list fetch.");

        var stderrLines = new List<string>();
        var stdoutBuilder = new System.Text.StringBuilder();

        // Read stderr line-by-line for progress (uv outputs package downloads here)
        var stderrTask = Task.Run(async () =>
            {
                var reader = process.StandardError;
                while (true)
                {
                    var line = await reader.ReadLineAsync(linkedCts.Token).ConfigureAwait(false);
                    if (line == null) break;
                    stderrLines.Add(line);
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        var trimmed = line.Trim();
                        // uv spinner characters indicate active work
                        bool isSpinner = trimmed.Length > 0 && (
                            trimmed[0] == '\u2801' || trimmed[0] == '\u2809' ||
                            trimmed[0] == '\u2819' || trimmed[0] == '\u2838' ||
                            trimmed[0] == '\u2834' || trimmed[0] == '\u2826' ||
                            trimmed[0] == '\u2827' || trimmed[0] == '\u2807' ||
                            trimmed[0] == '\u280F' || trimmed[0] == '\u280B');

                        if (trimmed.Contains("Downloading", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.Contains("Building", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.Contains("Resolved", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.Contains("Installed", StringComparison.OrdinalIgnoreCase) ||
                            isSpinner)
                        {
                            progressCallback?.Invoke("Resolving packages", trimmed, null);
                        }
                        else if (trimmed.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                                 trimmed.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                                 trimmed.Contains("Error", StringComparison.Ordinal) ||
                                 trimmed.Contains("Traceback", StringComparison.Ordinal))
                        {
                            // Kill process immediately — no point waiting for timeout
                            try { if (!process.HasExited) process.Kill(true); } catch { }

                            var errorDetail = string.Join("\n", stderrLines);
                            progressCallback?.Invoke("Error encountered", trimmed, errorDetail);
                            // Continue reading remaining lines briefly then break
                        }
                    }
                }
            }, linkedCts.Token);

            // Read stdout: should contain a single JSON array line
            var stdoutTask = Task.Run(async () =>
            {
                var reader = process.StandardOutput;
                while (true)
                {
                    var line = await reader.ReadLineAsync(linkedCts.Token).ConfigureAwait(false);
                    if (line == null) break;
                    stdoutBuilder.AppendLine(line);
                }
            }, linkedCts.Token);

            await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(linkedCts.Token))
                .ConfigureAwait(false);

            var stdoutText = stdoutBuilder.ToString();
            var stderrText = string.Join("\n", stderrLines);

            if (process.ExitCode != 0)
            {
                var errorDetail = !string.IsNullOrWhiteSpace(stderrText) ? stderrText : stdoutText;
                throw new InvalidOperationException($"uv exit={process.ExitCode}: {errorDetail.Trim()}");
            }

            // Parse JSON array of model names from stdout
            // NB: stdout may have uv info lines prefixed; extract the last JSON array
            var stdoutTrimmed = stdoutText.Trim();
            var jsonStart = stdoutTrimmed.LastIndexOf('[');
            var jsonEnd = stdoutTrimmed.LastIndexOf(']');
            string jsonText;
            if (jsonStart >= 0 && jsonEnd > jsonStart)
                jsonText = stdoutTrimmed[jsonStart..(jsonEnd + 1)];
            else
                jsonText = stdoutTrimmed;

            var modelNames = JsonSerializer.Deserialize<List<string>>(jsonText);
            if (modelNames == null || modelNames.Count == 0)
                return null;

            return modelNames
            .Select(name => CoquiTtsModelInfo.FromModelName(name))
            .OrderBy(m => m.Name)
            .ToList();
    }

    public CoquiTtsModelManager(string? dataDir, IStreamShellHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _projectDir = Path.Combine(
            dataDir ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".openclaw-ptt"),
            "coqui-tts-env");
        Directory.CreateDirectory(_projectDir);
    }

    /// <summary>Converts a Coqui model name like tts_models/en/ljspeech/vits to a HuggingFace cache dir slug.</summary>
    private static string ModelToCacheSlug(string modelName)
    {
        // Coqui models are at: coqui/TTS → ... tts_models/en/ljspeech/vits
        // The HF cache dir is: models--coqui--TTS
        return "models--coqui--TTS";
    }

    /// <summary>Check if a model's files are cached in the HuggingFace hub.</summary>
    public static bool IsModelCached(string modelName)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var cacheDir = Path.Combine(home, ".cache", "huggingface", "hub", ModelToCacheSlug(modelName));

        if (!Directory.Exists(cacheDir))
            return false;

        // Check if the model path exists in any snapshot
        var snapshots = Path.Combine(cacheDir, "snapshots");
        if (Directory.Exists(snapshots))
        {
            foreach (var snap in Directory.EnumerateDirectories(snapshots))
            {
                var modelPath = Path.Combine(snap, modelName);
                if (Directory.Exists(modelPath))
                    return true;
                // Some models store as single .pth file
                var pthFile = modelPath + ".pth";
                if (File.Exists(pthFile))
                    return true;
            }
        }

        // Also check blobs directory (some HF cache layouts)
        var blobs = Path.Combine(cacheDir, "blobs");
        if (Directory.Exists(blobs) && Directory.EnumerateFiles(blobs).Any())
            return true;

        return false;
    }

    /// <summary>Lists names of locally cached Coqui TTS models (from known list).</summary>
    public static IReadOnlyList<string> GetCachedModels()
    {
        return FallbackModels
            .Where(m => IsModelCached(m.Name))
            .Select(m => m.Name)
            .ToList();
    }

    /// <summary>
    /// Pre-downloads a Coqui TTS model by invoking <c>uv run python -c</c>
    /// to instantiate TTS(model_name). This triggers HuggingFace download.
    /// Logs all uv/Python output for debugging via <c>host.AddMessage</c>.
    /// </summary>
    public async Task DownloadModelAsync(
        string modelName,
        Action<string, string, long?, long?, bool>? progressCallback = null,
        CancellationToken ct = default)
    {
        if (IsModelCached(modelName))
        {
            progressCallback?.Invoke(modelName, "Already cached", null, null, true);
            return;
        }

        _host.AddMessage($"[grey]    Starting download of {modelName}...[/]");
        progressCallback?.Invoke(modelName, "Starting download (uv resolving packages)...", null, null, false);

        var pythonCmd = CoquiUvEnvironment.BuildPreDownloadCommand(modelName);
        var uvPath = CoquiUvEnvironment.FindUv() ?? "uv";
        var escapedCmd = pythonCmd.Replace("\\", "\\\\").Replace("\"", "\\\"");

        var psi = new ProcessStartInfo
        {
            FileName = uvPath,
            Arguments = $"run --directory \"{_projectDir}\" python -c \"{escapedCmd}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var timeoutCts = new CancellationTokenSource(_downloadTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        _host.AddMessage($"[grey]    Running: uv run --directory ... python -c ...[/]");
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start uv run for Coqui TTS model download.");

        // Collect all stdout/stderr for debugging
        var stdoutLines = new List<string>();
        var stderrLines = new List<string>();

        try
        {
            // Read both stdout and stderr, logging progress
            var stdoutTask = Task.Run(async () =>
            {
                var reader = process.StandardOutput;
                while (true)
                {
                    var line = await reader.ReadLineAsync(linkedCts.Token).ConfigureAwait(false);
                    if (line == null) break;
                    stdoutLines.Add(line);
                    // Log for user visibility
                    if (!string.IsNullOrWhiteSpace(line))
                        _host.AddMessage($"[grey]      [stdout] {line}[/]");
                    if (line.Contains("%", StringComparison.Ordinal) || line.Contains("Download", StringComparison.OrdinalIgnoreCase))
                        progressCallback?.Invoke(modelName, $"Downloading: {line.Trim()[..Math.Min(line.Trim().Length, 80)]}", null, null, false);
                }
            }, linkedCts.Token);

            var stderrTask = Task.Run(async () =>
            {
                var reader = process.StandardError;
                while (true)
                {
                    var line = await reader.ReadLineAsync(linkedCts.Token).ConfigureAwait(false);
                    if (line == null) break;
                    stderrLines.Add(line);
                    // Log uv/Python stderr for user visibility
                    if (!string.IsNullOrWhiteSpace(line))
                        _host.AddMessage($"[grey]      [stderr] {line}[/]");
                    // HF download progress often shows percentages on stderr
                    if (line.Contains("%", StringComparison.Ordinal) || line.Contains("Download", StringComparison.OrdinalIgnoreCase) || line.Contains("Fetching", StringComparison.OrdinalIgnoreCase))
                        progressCallback?.Invoke(modelName, $"Downloading: {line.Trim()[..Math.Min(line.Trim().Length, 80)]}", null, null, false);
                }
            }, linkedCts.Token);

            await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(linkedCts.Token))
                .ConfigureAwait(false);

            var stdoutText = string.Join("\n", stdoutLines);
            var stderrText = string.Join("\n", stderrLines);

            if (process.ExitCode != 0)
            {
                var errorDetail = !string.IsNullOrWhiteSpace(stderrText) ? stderrText : stdoutText;
                _host.AddMessage($"[red]    Download failed (exit={process.ExitCode}): {errorDetail.Trim()}[/]");
                progressCallback?.Invoke(modelName, $"Failed (exit={process.ExitCode})", null, null, false);
                throw new InvalidOperationException($"Coqui TTS download failed (exit={process.ExitCode}): {errorDetail.Trim()}");
            }

            _host.AddMessage($"[grey]    Process exited OK. Checking cache...[/]");
            var isCached = IsModelCached(modelName);
            if (isCached)
            {
                _host.AddMessage($"[green]    ✓ Model {modelName} cached successfully.[/]");
            }
            else
            {
                _host.AddMessage($"[yellow]    ⚠ Process completed but model not found in cache. stdout/stderr above may help diagnose.[/]");
            }
            progressCallback?.Invoke(modelName,
                isCached ? "Download complete" : "Process completed but model not found in cache",
                null, null, isCached);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            _host.AddMessage("[yellow]    Download cancelled.[/]");
            progressCallback?.Invoke(modelName, "Cancelled", null, null, false);
            throw;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            _host.AddMessage($"[red]    Download error: {ex.Message}[/]");
            progressCallback?.Invoke(modelName, $"Failed: {ex.Message}", null, null, false);
            throw;
        }
    }

    /// <summary>Deletes a cached Coqui TTS model from the HuggingFace cache.</summary>
    public static bool DeleteModel(string modelName)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var cacheDir = Path.Combine(home, ".cache", "huggingface", "hub", ModelToCacheSlug(modelName));

        if (!Directory.Exists(cacheDir))
            return false;

        // Delete the model directory from all snapshots
        var snapshots = Path.Combine(cacheDir, "snapshots");
        var deleted = false;
        if (Directory.Exists(snapshots))
        {
            foreach (var snap in Directory.EnumerateDirectories(snapshots))
            {
                var modelPath = Path.Combine(snap, modelName);
                if (Directory.Exists(modelPath))
                {
                    try { Directory.Delete(modelPath, recursive: true); deleted = true; } catch { }
                }
                var pthFile = modelPath + ".pth";
                if (File.Exists(pthFile))
                {
                    try { File.Delete(pthFile); deleted = true; } catch { }
                }
            }
        }
        return deleted;
    }
}

/// <summary>Info about an available Coqui TTS model.</summary>
public sealed class CoquiTtsModelInfo
{
    public string Name { get; }
    public string Description { get; }

    public CoquiTtsModelInfo(string name, string description)
    {
        Name = name;
        Description = description;
    }

    /// <summary>
    /// Derives a user-friendly description from the model path segments.
    /// E.g. "tts_models/en/ljspeech/vits" → "English · LJSpeech · VITS"
    /// </summary>
    public static CoquiTtsModelInfo FromModelName(string modelName)
    {
        var parts = modelName.Split('/');
        if (parts.Length < 3)
            return new CoquiTtsModelInfo(modelName, modelName);

        // tts_models / <lang> / <dataset> / <architecture>
        var lang = parts.Length > 1 ? parts[1] : "";
        var dataset = parts.Length > 2 ? parts[2] : "";
        var arch = parts.Length > 3 ? parts[3] : "";

        var desc = string.Join(" · ", new[] { lang, dataset, arch }
            .Where(s => !string.IsNullOrEmpty(s)));
        return new CoquiTtsModelInfo(modelName, desc);
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;

namespace OpenClawPTT.TTS.Providers;

/// <summary>
/// Manages Coqui TTS models via <c>uv</c> — listing, pre-downloading, and deleting.
/// Coqui TTS models are HuggingFace models (e.g. <c>tts_models/en/ljspeech/vits</c>)
/// auto-downloaded to the HuggingFace cache on first use.
/// </summary>
public sealed class CoquiTtsModelManager
{
    private readonly string _projectDir;
    private readonly IStreamShellHost _host;
    private readonly TimeSpan _downloadTimeout = TimeSpan.FromMinutes(30);

    /// <summary>Well-known Coqui TTS models available for selection.</summary>
    public static readonly IReadOnlyList<CoquiTtsModelInfo> AvailableModels = new List<CoquiTtsModelInfo>
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
        return AvailableModels
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
}

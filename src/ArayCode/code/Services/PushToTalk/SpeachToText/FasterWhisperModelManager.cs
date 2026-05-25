using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArayCode.Services;
using ArayCode.Services.Themes;

namespace ArayCode.Transcriber;

/// <summary>
/// Manages <c>faster-whisper</c> models via <c>uv</c>.
///
/// <para>
/// faster-whisper uses CTranslate2 models auto-downloaded to the HuggingFace cache
/// (~/.cache/huggingface/hub/). The download is triggered on first WhisperModel() call.
/// This manager provides listing, pre-downloading, and deletion of these models.
/// </para>
///
/// <para>
/// Unlike the legacy <see cref="WhisperCppModelManager"/> which handles both
/// .bin files (C++) and .pt files (Python openai-whisper), this manager is
/// focused solely on faster-whisper's CTranslate2 models.
/// </para>
/// </summary>
public sealed class FasterWhisperModelManager
{
    private readonly FasterWhisperEnvironment _environment;
    private readonly IStreamShellHost _host;
    private readonly TimeSpan _downloadTimeout = TimeSpan.FromMinutes(30);

    /// <summary>All known faster-whisper models available for download.</summary>
    public static readonly IReadOnlyList<WhisperModelInfo> AvailableModels = new List<WhisperModelInfo>
    {
        // Same model sizes as openai-whisper, but using CTranslate2 format
        new("tiny",               "Tiny (39 MB) — fastest, lowest accuracy"),
        new("tiny.en",            "Tiny English (39 MB) — English-only"),
        new("base",               "Base (74 MB) — good balance for quick use"),
        new("base.en",            "Base English (74 MB) — English-only"),
        new("small",              "Small (244 MB) — decent accuracy"),
        new("small.en",           "Small English (244 MB) — English-only"),
        new("medium",             "Medium (769 MB) — good accuracy"),
        new("medium.en",          "Medium English (769 MB) — English-only"),
        new("large-v1",           "Large v1 (1.5 GB) — high accuracy"),
        new("large-v2",           "Large v2 (1.5 GB) — improved"),
        new("large-v3",           "Large v3 (1.5 GB) — latest large model"),
        // faster-whisper specific models
        new("distil-large-v2",    "Distil Large v2 (756 MB) — distilled, fast, good accuracy"),
        new("distil-medium.en",   "Distil Medium English (394 MB) — distilled, English-only"),
        new("distil-small.en",    "Distil Small English (166 MB) — fastest distilled"),
    };

    // ── Known HuggingFace model IDs for faster-whisper ──────────────
    // The CTranslate2-format models live under Systran/faster-whisper-<model>
    // and for distil variants — under a separate model ID.
    private static readonly Dictionary<string, string> ModelIdMap = new(StringComparer.Ordinal)
    {
        ["tiny"]                = "Systran/faster-whisper-tiny",
        ["tiny.en"]             = "Systran/faster-whisper-tiny.en",
        ["base"]                = "Systran/faster-whisper-base",
        ["base.en"]             = "Systran/faster-whisper-base.en",
        ["small"]               = "Systran/faster-whisper-small",
        ["small.en"]            = "Systran/faster-whisper-small.en",
        ["medium"]              = "Systran/faster-whisper-medium",
        ["medium.en"]           = "Systran/faster-whisper-medium.en",
        ["large-v1"]            = "Systran/faster-whisper-large-v1",
        ["large-v2"]            = "Systran/faster-whisper-large-v2",
        ["large-v3"]            = "Systran/faster-whisper-large-v3",
        ["distil-large-v2"]     = "Systran/faster-distil-whisper-large-v2",
        ["distil-medium.en"]    = "Systran/faster-distil-whisper-medium.en",
        ["distil-small.en"]     = "Systran/faster-distil-whisper-small.en",
    };

    public FasterWhisperModelManager(FasterWhisperEnvironment environment, IStreamShellHost host)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>Gets the HuggingFace model ID for a given model name.</summary>
    public static string GetModelId(string modelName)
    {
        return ModelIdMap.TryGetValue(modelName, out var id)
            ? id
            : $"Systran/faster-whisper-{modelName}";
    }

    /// <summary>
    /// Checks whether a faster-whisper model is already cached locally
    /// in the HuggingFace hub cache.
    /// </summary>
    public static bool IsModelCached(string modelName)
    {
        var modelId = GetModelId(modelName);
        return IsModelIdCached(modelId);
    }

    /// <summary>
    /// Checks whether a specific HuggingFace model ID is cached.
    /// The cache directory uses the format: <c>models--org--repo-name</c>.
    /// A model is considered cached if the directory exists and has content
    /// beyond the bare refs/ structure (indicating actual model files).
    /// </summary>
    private static bool IsModelIdCached(string modelId)
    {
        var cacheDir = GetHuggingFaceCacheDir(modelId);
        if (!Directory.Exists(cacheDir))
            return false;

        // Check if there are actual model files (not just .git refs)
        var snapshots = Path.Combine(cacheDir, "snapshots");
        if (Directory.Exists(snapshots))
        {
            foreach (var sub in Directory.EnumerateDirectories(snapshots))
            {
                if (Directory.EnumerateFiles(sub).Any())
                    return true;
            }
        }

        var blobs = Path.Combine(cacheDir, "blobs");
        if (Directory.Exists(blobs) && Directory.EnumerateFiles(blobs).Any())
            return true;

        return false;
    }

    /// <summary>Lists names of locally cached faster-whisper models.</summary>
    public static IReadOnlyList<string> GetCachedModels()
    {
        var cached = new List<string>();
        foreach (var (name, modelId) in ModelIdMap)
        {
            if (IsModelIdCached(modelId))
                cached.Add(name);
        }
        return cached;
    }

    /// <summary>
    /// Gets the HuggingFace cache directory for a model.
    /// E.g. <c>Systran/faster-whisper-base</c> → <c>~/.cache/huggingface/hub/models--Systran--faster-whisper-base</c>
    /// </summary>
    public static string GetHuggingFaceCacheDir(string modelId)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var slug = modelId.Replace("/", "--");
        return Path.Combine(home, ".cache", "huggingface", "hub", $"models--{slug}");
    }

    /// <summary>
    /// Pre-downloads a faster-whisper model by instantiating WhisperModel
    /// through <c>uv run</c>. This triggers the CTranslate2 model download
    /// to the HuggingFace cache.
    /// </summary>
    /// <param name="modelName">Model name (e.g. "base", "small.en").</param>
    /// <param name="progressCallback">(fileName, status, downloadedBytes?, totalBytes?, isComplete)</param>
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

        _host.AddMessage($"[{ThemeProvider.Current.Tools.General.Muted}]    Starting download of faster-whisper/{modelName}...[/]");
        progressCallback?.Invoke(modelName, "Starting download (uv resolving)...", null, null, false);

        var pythonCmd = FasterWhisperEnvironment.BuildPreDownloadCommand(modelName);
        var psi = _environment.CreateProcessStartInfo(pythonCmd);
        psi.RedirectStandardError = true;

        using var timeoutCts = new CancellationTokenSource(_downloadTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start uv run for model download: {modelName}");

        // Collect all output for debugging
        var stdoutLines = new System.Collections.Generic.List<string>();
        var stderrLines = new System.Collections.Generic.List<string>();

        try
        {
            var stdoutTask = Task.Run(async () =>
            {
                var reader = process.StandardOutput;
                while (true)
                {
                    var line = await reader.ReadLineAsync(linkedCts.Token).ConfigureAwait(false);
                    if (line == null) break;
                    stdoutLines.Add(line);
                    if (!string.IsNullOrWhiteSpace(line))
                        _host.AddMessage($"[{ThemeProvider.Current.Tools.General.Muted}]      [[stdout]] {line}[/]");
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
                    if (!string.IsNullOrWhiteSpace(line))
                        _host.AddMessage($"[{ThemeProvider.Current.Tools.General.Muted}]      [[stderr]] {line}[/]");
                    if (line.Contains("%", StringComparison.Ordinal) || line.Contains("Download", StringComparison.OrdinalIgnoreCase))
                        progressCallback?.Invoke(modelName, "Downloading...", null, null, false);
                }
            }, linkedCts.Token);

            await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(linkedCts.Token))
                .ConfigureAwait(false);

            var stderrText = string.Join("\n", stderrLines);

            if (process.ExitCode != 0)
            {
                _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Error}]    Download failed (exit={process.ExitCode}): {stderrText.Trim()}[/]");
                progressCallback?.Invoke(modelName, $"Failed (exit={process.ExitCode})", null, null, false);
                throw new InvalidOperationException(
                    $"faster-whisper model download failed (exit={process.ExitCode}): {stderrText.Trim()}");
            }

            _host.AddMessage($"[{ThemeProvider.Current.Tools.General.Muted}]    Process exited OK. Checking cache...[/]");
            var isCached = IsModelCached(modelName);
            if (isCached)
            {
                _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Success}]    ✓ Model {modelName} cached successfully.[/]");
                progressCallback?.Invoke(modelName, "Download complete", null, null, true);
            }
            else
            {
                _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]    ⚠ Process completed but model not found in cache. See stdout/stderr above.[/]");
                progressCallback?.Invoke(modelName,
                    "Process completed but model not found in cache", null, null, false);
            }
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]    Download cancelled.[/]");
            progressCallback?.Invoke(modelName, "Cancelled", null, null, false);
            throw;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Error}]    Download error: {ex.Message}[/]");
            progressCallback?.Invoke(modelName, $"Failed: {ex.Message}", null, null, false);
            throw;
        }
    }

    /// <summary>
    /// Deletes a cached faster-whisper model from the HuggingFace cache.
    /// </summary>
    /// <returns>True if the model existed and was deleted.</returns>
    public static bool DeleteModel(string modelName)
    {
        var modelId = GetModelId(modelName);
        var cacheDir = GetHuggingFaceCacheDir(modelId);
        if (!Directory.Exists(cacheDir))
            return false;

        try
        {
            Directory.Delete(cacheDir, recursive: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ── Progress parsing ────────────────────────────────────────────
    // Progress is now logged inline in DownloadModelAsync via host.AddMessage
    // for both stdout and stderr. The old ReadDownloadProgressAsync is removed.
}

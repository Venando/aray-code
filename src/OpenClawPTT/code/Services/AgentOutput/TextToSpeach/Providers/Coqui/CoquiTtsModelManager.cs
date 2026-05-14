using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenClawPTT.Services;

namespace OpenClawPTT.TTS.Providers;

/// <summary>
/// Manages Coqui TTS models via <c>uv</c> — listing, pre-downloading, and deleting.
/// Models are fetched live from the <c>TTS</c> package (<c>uv run python -c "..."</c>).
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
    /// Human-readable reason from the last fetch attempt (set even on success).
    /// Empty if the last call hasn't been made yet.
    /// </summary>
    public static string LastFetchErrorDetail { get; private set; } = string.Empty;

    /// <summary>
    /// Returns the live model list from <c>TTS</c> package if uv is available
    /// and fetch succeeds. Returns empty list on failure with error in
    /// <see cref="LastFetchErrorDetail"/>.
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
            LastFetchErrorDetail = "uv is not installed";
            host.AddMessage("[red]    \u2717 uv is not installed \u2014 cannot fetch model list.[/]");
            host.AddMessage($"[grey]      Install: {CoquiUvEnvironment.GetInstallInstructions()}[/]");
            return Array.Empty<CoquiTtsModelInfo>();
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
                    LastFetchErrorDetail = string.Empty;
                    setupPanel.SetCompleted(true, $"Found {liveList.Count} models");
                    lock (s_liveLock) { s_liveModels = liveList; }
                    host.AddMessage($"[green]    \u2713 Found {liveList.Count} models live from Coqui TTS.[/]");
                    return liveList;
                }

                LastFetchErrorDetail = "TTS package returned no models";
                setupPanel.SetCompleted(false, "No models returned");
            }
            finally
            {
                await Task.Delay(500, CancellationToken.None);
                host.ResetBottomPanel();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            LastFetchErrorDetail = "User cancelled";
            host.ResetBottomPanel();
            throw;
        }
        catch (OperationCanceledException)
        {
            LastFetchErrorDetail = "Live model fetch timed out after 5 minutes";
            host.AddMessage("[red]    \u2717 Live model fetch timed out after 5 minutes.[/]");
            host.AddMessage("[grey]      Check network and uv/Python setup, then re-run /reconfigure TTS.[/]");
        }
        catch (Exception ex)
        {
            var errorMsg = ex.Message;
            host.AddMessage("[red]    \u2717 Live model fetch failed. Error from uv:[/]");

            var errorLines = errorMsg.Split('\n');
            var shown = 0;
            foreach (var line in errorLines)
            {
                if (shown >= 20) { host.AddMessage("[red]      ... (output truncated)[/]"); break; }
                var trimmedLine = line.Trim();
                if (!string.IsNullOrWhiteSpace(trimmedLine))
                {
                    host.AddMessage($"[red]      {CoquiMarkupHelper.EscapeSpectreMarkup(trimmedLine)}[/]");
                    shown++;
                }
            }

            LastFetchErrorDetail = errorMsg;
            host.AddMessage("[grey]      Fix the uv/Python issues above and re-run /reconfigure TTS to get models.[/]");
        }

        return Array.Empty<CoquiTtsModelInfo>();
    }

    /// <summary>
    /// Fetches the full model list from the installed <c>TTS</c> package via <c>uv run python</c>.
    /// </summary>
    private static async Task<IReadOnlyList<CoquiTtsModelInfo>?> FetchFromUvAsync(
        IStreamShellHost host,
        string? dataDir,
        Action<string, string, string?>? progressCallback,
        CancellationToken ct)
    {
        var projectDir = CoquiUvEnvironment.GetProjectDir(dataDir);

        CoquiUvEnvironment.EnsureProjectFiles(projectDir);
        CoquiUvEnvironment.EnsureVenvPythonMatches(projectDir);

        var uvPath = CoquiUvEnvironment.ResolveUvPath();
        var cmd = "from TTS.api import TTS; import json; " +
                  "models = TTS().list_models().list_models(); " +
                  "print(json.dumps(models))";

        var psi = new ProcessStartInfo
        {
            FileName = uvPath,
            Arguments = $"run{CoquiUvEnvironment.GetPythonArg()} --directory \"{projectDir}\" python -c \"{cmd}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        host.AddMessage($"[grey]      Fetching model list from coqui/TTS on HuggingFace...[/]");
        progressCallback?.Invoke("Resolving packages", "uv setting up Python environment...", null);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start uv run for model list fetch.");

        var stderrLines = new List<string>();
        var stdoutBuilder = new StringBuilder();

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
                            try { if (!process.HasExited) process.Kill(true); } catch { }
                            var relevantLines = stderrLines
                                .Where(l => !string.IsNullOrWhiteSpace(l))
                                .Select(l => l.Trim())
                                .Reverse().Take(5).Reverse()
                                .ToArray();
                            var errorDetail = string.Join("\n", relevantLines);
                            progressCallback?.Invoke("Error encountered", trimmed, errorDetail);
                        }
                    }
                }
            }, linkedCts.Token);

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

            var jsonText = ExtractJsonArray(stdoutText);

            var modelNames = JsonSerializer.Deserialize<List<string>>(jsonText);
            if (modelNames == null || modelNames.Count == 0)
                return null;

            var liveList = modelNames
            .Select(name => CoquiTtsModelInfo.FromModelName(name))
            .OrderBy(m => m.Name)
            .ToList();

            CoquiUvEnvironment.ClearBrokenFlagKeepPython();

            return liveList;
    }

    public CoquiTtsModelManager(string? dataDir, IStreamShellHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _projectDir = CoquiUvEnvironment.GetProjectDir(dataDir);
        Directory.CreateDirectory(_projectDir);
    }

    /// <summary>
    /// Returns model names that are currently cached on disk by running
    /// a Python command that walks the HuggingFace cache directly.
    /// Much more reliable than C# filesystem traversal.
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetCachedModelsAsync(
        IStreamShellHost host, string? dataDir, CancellationToken ct = default)
    {
        var projectDir = CoquiUvEnvironment.GetProjectDir(dataDir);

        if (!CoquiUvEnvironment.IsUvAvailable())
        {
            host.AddMessage("[grey]    uv not available[/]");
            return Array.Empty<string>();
        }

        host.AddMessage($"[grey]    Running Python cache scan...[/]");

        // Write the Python script to a temp file (avoids -c escaping/syntax issues)
        var scriptPath = Path.Combine(projectDir, "list_cached.py");
        File.WriteAllText(scriptPath, CoquiUvEnvironment.ListCachedModelPathsScript());

        var uvPath = CoquiUvEnvironment.ResolveUvPath();

        var psi = new ProcessStartInfo
        {
            FileName = uvPath,
            Arguments = $"run{CoquiUvEnvironment.GetPythonArg()} --directory \"{projectDir}\" python \"{scriptPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            using var process = Process.Start(psi);
            if (process == null)
            {
                host.AddMessage("[red]   Failed to start python for models check[/]");
                return Array.Empty<string>();
            }

            var stdout = await process.StandardOutput.ReadToEndAsync(linkedCts.Token);
            var stderr = await process.StandardError.ReadToEndAsync(linkedCts.Token);
            await process.WaitForExitAsync(linkedCts.Token);

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                foreach (var line in stderr.Split('\n'))
                {
                    var t = line.Trim();
                    if (!string.IsNullOrEmpty(t))
                        host.AddMessage($"[grey]    Models directory: {CoquiMarkupHelper.EscapeSpectreMarkup(t[..Math.Min(t.Length, 120)])}[/]");
                }
            }

            if (process.ExitCode != 0)
            {
                host.AddMessage($"[red]    Failed to run model folder stderr excerpt: {CoquiMarkupHelper.EscapeSpectreMarkup(stderr.Trim()[..Math.Min(stderr.Trim().Length, 200)])}[/]");
                return Array.Empty<string>();
            }

            var trimmed = stdout.Trim();

            if (string.IsNullOrEmpty(trimmed))
            {
                host.AddMessage("[yellow]    Nothing found[/]");
                return Array.Empty<string>();
            }

            var jsonText = ExtractJsonArray(stdout);

            var names = JsonSerializer.Deserialize<List<string>>(jsonText);
            if (names == null)
            {
                host.AddMessage("[yellow]   Json deserialize error[/]");
                return Array.Empty<string>();
            }

            host.AddMessage($"[green]    Found {names.Count} cached models[/]");

            // Successful uv run proves the environment works — clear any stale
            // broken flag set by a previous TTS service startup failure.
            CoquiUvEnvironment.ClearBrokenFlagKeepPython();

            return names;
        }
        catch (Exception ex)
        {
            host.AddMessage($"[red]    Get Cached models exception: {CoquiMarkupHelper.EscapeSpectreMarkup(ex.Message)}[/]");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Check if a model's files are cached in the Coqui TTS storage directory.
    /// Coqui stores models in <c>%LOCALAPPDATA%/tts</c> (Windows) or
    /// <c>~/.local/share/tts</c> (Linux/macOS) with <c>--</c> separators.
    /// </summary>
    public static bool IsModelCached(string modelName)
    {
        var modelDir = GetModelDir(modelName);
        return modelDir != null && Directory.EnumerateFileSystemEntries(modelDir).Any();
    }

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

        if (CoquiUvEnvironment.IsUvBuildBroken)
        {
            var detail = CoquiUvEnvironment.UvBuildErrorDetail ?? "uv environment is broken";
            _host.AddMessage($"[red]    Cannot download \u2014 uv environment is broken: {CoquiMarkupHelper.EscapeSpectreMarkup(detail)}[/]");
            throw new InvalidOperationException($"uv environment is broken: {detail}");
        }

        _host.AddMessage($"[grey]    Starting download of {modelName}...[/]");
        progressCallback?.Invoke(modelName, "Starting download (uv resolving packages)...", null, null, false);

        var pythonCmd = CoquiUvEnvironment.BuildPreDownloadCommand(modelName);
        var uvPath = CoquiUvEnvironment.ResolveUvPath();
        var escapedCmd = pythonCmd.Replace("\\", "\\\\").Replace("\"", "\\\"");

        var psi = new ProcessStartInfo
        {
            FileName = uvPath,
            Arguments = $"run{CoquiUvEnvironment.GetPythonArg()} --directory \"{_projectDir}\" python -c \"{escapedCmd}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var timeoutCts = new CancellationTokenSource(_downloadTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start uv run for Coqui TTS model download.");

        var stdoutLines = new List<string>();
        var stderrLines = new List<string>();

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
                        _host.AddMessage($"[grey]      [[stdout]] {line}[/]");
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
                    if (!string.IsNullOrWhiteSpace(line))
                        _host.AddMessage($"[grey]      {CoquiMarkupHelper.EscapeSpectreMarkup(line)}[/]");
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
                var summary = CoquiUvEnvironment.SummarizeBuildError(errorDetail);

                if (CoquiUvEnvironment.IsBuildError(errorDetail))
                    CoquiUvEnvironment.MarkUvBuildBroken(summary);

                _host.AddMessage($"[red]    Download failed (exit={process.ExitCode}): {CoquiMarkupHelper.EscapeSpectreMarkup(summary)}[/]");
                progressCallback?.Invoke(modelName, $"Failed (exit={process.ExitCode})", null, null, false);
                throw new InvalidOperationException($"Coqui TTS download failed (exit={process.ExitCode}): {summary}");
            }

            var okInStdout = stdoutText.Contains("OK", StringComparison.Ordinal);
            var isCached = okInStdout || IsModelCached(modelName);

            // Successful download proves the environment works — clear any stale broken flag.
            CoquiUvEnvironment.ClearBrokenFlagKeepPython();

            if (isCached)
            {
                // Some Coqui models (e.g. jenny) are distributed as ZIP archives.
                // Extract them so TTS can find the model files at the expected level.
                var extracted = CoquiTtsZipExtractor.ExtractModelZips(modelName);
                if (extracted > 0)
                    _host.AddMessage($"[green]    \u2713 Model {modelName} cached (extracted {extracted} archive(s)).[/]");
                else
                    _host.AddMessage($"[green]    \u2713 Model {modelName} cached successfully.[/]");
            }
            else
            {
                _host.AddMessage($"[yellow]    \u26a0 Process completed but model not found in cache. stdout/stderr above may help diagnose.[/]");
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

    /// <summary>
    /// Fetches model download sizes by looking up download URLs from the Coqui TTS
    /// model registry and making HEAD requests for Content-Length. Uses a Python
    /// helper script from the installed TTS environment.
    /// First call may take ~15 s for 1000+ models (parallel HEAD requests),
    /// subsequent calls are instant from cache.
    /// Returns a dictionary of model name → size in bytes (missing = no size).
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, long>> FetchHuggingFaceSizesAsync(
        IStreamShellHost host,
        IReadOnlyList<string> modelNames,
        string? dataDir,
        CancellationToken ct = default)
    {
        host.AddMessage($"[grey]      [diag] FetchHuggingFaceSizesAsync: entered, {modelNames.Count} model names[/]");

        if (modelNames.Count == 0)
        {
            host.AddMessage("[grey]      [diag] 0 model names, returning empty[/]");
            return new Dictionary<string, long>();
        }

        var projectDir = CoquiUvEnvironment.GetProjectDir(dataDir);
        host.AddMessage($"[grey]      [diag] projectDir={projectDir}, exists={Directory.Exists(projectDir)}[/]");

        var uvAvailable = CoquiUvEnvironment.IsUvAvailable();
        host.AddMessage($"[grey]      [diag] IsUvAvailable={uvAvailable}, ResolveUvPath={CoquiUvEnvironment.ResolveUvPath()}[/]");
        if (!uvAvailable)
        {
            host.AddMessage("[yellow]      [diag] uv not available, returning empty[/]");
            return new Dictionary<string, long>();
        }

        // Write model names to a temp JSON file
        var namesPath = Path.Combine(projectDir, "model_names.json");
        File.WriteAllText(namesPath, JsonSerializer.Serialize(modelNames));
        host.AddMessage($"[grey]      [diag] Wrote {modelNames.Count} names to {namesPath}[/]");

        var cachePath = Path.Combine(projectDir, "model_download_sizes_cache.json");

        // Write the Python helper script
        var scriptPath = Path.Combine(projectDir, "fetch_model_sizes.py");
        File.WriteAllText(scriptPath, CoquiUvEnvironment.HfSizesScript());
        host.AddMessage($"[grey]      [diag] Wrote Python script to {scriptPath}, existsAfter={File.Exists(scriptPath)}[/]");

        // Load existing cache to show progress
        var existingCount = 0;
        var cacheExists = File.Exists(cachePath);
        host.AddMessage($"[grey]      [diag] cachePath={cachePath}, exists={cacheExists}[/]");
        if (cacheExists)
        {
            try
            {
                var existing = JsonSerializer.Deserialize<Dictionary<string, long>>(
                    File.ReadAllText(cachePath));
                existingCount = existing?.Count ?? 0;
                host.AddMessage($"[grey]      [diag] Loaded {existingCount} cached sizes[/]");
            }
            catch (Exception ex)
            {
                host.AddMessage($"[grey]      [diag] Failed to parse cache: {ex.Message}[/]");
            }
        }

        var missingCount = modelNames.Count - existingCount;
        var statusMsg = missingCount > 0
            ? $"Fetching download sizes for {missingCount} new models..."
            : "Loading model sizes from cache...";
        host.AddMessage($"[grey]      {statusMsg} (total={modelNames.Count}, cached={existingCount}, missing={missingCount})[/]");

        var uvPath = CoquiUvEnvironment.ResolveUvPath();
        var pythonArg = CoquiUvEnvironment.GetPythonArg();
        var psiArgs = $"run{pythonArg} --directory \"{projectDir}\" python \"{scriptPath}\" \"{namesPath}\" \"{cachePath}\"";
        host.AddMessage($"[grey]      [diag] uv command: {uvPath} {psiArgs}[/]");

        var psi = new ProcessStartInfo
        {
            FileName = uvPath,
            Arguments = psiArgs,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            using var process = Process.Start(psi);
            if (process == null)
            {
                host.AddMessage("[red]      [diag] Process.Start returned null[/]");
                return new Dictionary<string, long>();
            }
            host.AddMessage($"[grey]      [diag] Python process started, PID={process.Id}, waiting...[/]");

            var stdout = await process.StandardOutput.ReadToEndAsync(linkedCts.Token);
            var stderr = await process.StandardError.ReadToEndAsync(linkedCts.Token);
            await process.WaitForExitAsync(linkedCts.Token);

            host.AddMessage($"[grey]      [diag] Python exited, code={process.ExitCode}[/]");
            host.AddMessage($"[grey]      [diag] stdout length={stdout.Length}, stderr length={stderr.Length}[/]");

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                var stderrPreview = stderr.Length > 500 ? stderr[..500] + "..." : stderr;
                host.AddMessage($"[grey]      [diag] stderr: {CoquiMarkupHelper.EscapeSpectreMarkup(stderrPreview)}[/]");
            }

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                var stdoutPreview = stdout.Length > 300 ? stdout[..300] + "..." : stdout;
                host.AddMessage($"[grey]      [diag] stdout preview: {CoquiMarkupHelper.EscapeSpectreMarkup(stdoutPreview)}[/]");
            }

            if (process.ExitCode != 0)
            {
                host.AddMessage($"[yellow]      Model size fetch exited with code {process.ExitCode}[/]");
                // Try to load from cache anyway
                if (File.Exists(cachePath))
                {
                    var cached = JsonSerializer.Deserialize<Dictionary<string, long>>(
                        File.ReadAllText(cachePath));
                    host.AddMessage($"[grey]      [diag] Loaded {(cached?.Count ?? 0)} from cache after error[/]");
                    return cached ?? new Dictionary<string, long>();
                }
                return new Dictionary<string, long>();
            }

            var jsonText = ExtractJsonArray(stdout);
            host.AddMessage($"[grey]      [diag] Extracted JSON, length={jsonText.Length}[/]");
            var sizes = JsonSerializer.Deserialize<Dictionary<string, long>>(jsonText);
            var found = sizes?.Count ?? 0;
            host.AddMessage($"[green]      Download sizes known for {found}/{modelNames.Count} models.[/]");
            if (found > 0 && sizes != null)
            {
                var sample = sizes.Take(3).Select(kv => $"{kv.Key}={kv.Value}");
                host.AddMessage($"[grey]      [diag] Sample sizes: {string.Join(", ", sample)}[/]");
            }
            return sizes ?? new Dictionary<string, long>();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            host.AddMessage("[grey]      [diag] Cancelled via ct[/]");
            throw;
        }
        catch (Exception ex)
        {
            host.AddMessage($"[yellow]      Model size fetch failed: {ex.Message}. Type={ex.GetType().Name}[/]");
            if (ex.InnerException != null)
                host.AddMessage($"[grey]      [diag] Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}[/]");
            if (File.Exists(cachePath))
            {
                try
                {
                    var cached = JsonSerializer.Deserialize<Dictionary<string, long>>(
                        File.ReadAllText(cachePath));
                    if (cached is { Count: > 0 })
                    {
                        host.AddMessage($"[grey]      Loaded {cached.Count} sizes from cache.[/]");
                        return cached;
                    }
                }
                catch { }
            }
            return new Dictionary<string, long>();
        }
    }

    /// <summary>
    /// Returns the total disk size (bytes) of a cached model's directory,
    /// or <c>null</c> if the model isn't cached or the directory is empty.
    /// </summary>
    internal static long? GetModelSizeBytes(string modelName)
    {
        var modelDir = GetModelDir(modelName);
        if (modelDir == null)
            return null;

        try
        {
            var total = new DirectoryInfo(modelDir)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
            return total > 0 ? total : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the Coqui TTS model directory path, or null if not found.
    /// Coqui stores models in <c>%LOCALAPPDATA%/tts</c> (Windows) or
    /// <c>~/.local/share/tts</c> (Linux/macOS) with <c>--</c> separators.
    /// Internal so <see cref="CoquiTtsZipExtractor"/> can use it (SRP).
    /// </summary>
    internal static string? GetModelDir(string modelName)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var ttsDir = Path.Combine(localAppData, "tts");
        if (!Directory.Exists(ttsDir))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            ttsDir = Path.Combine(home, ".local", "share", "tts");
            if (!Directory.Exists(ttsDir))
                return null;
        }

        var dir = Path.Combine(ttsDir, modelName.Replace("/", "--"));
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>Deletes a cached Coqui TTS model from the Coqui TTS storage dir.</summary>
    public static bool DeleteModel(string modelName)
    {
        var modelDir = GetModelDir(modelName);
        if (modelDir == null)
            return false;

        try { Directory.Delete(modelDir, recursive: true); return true; }
        catch { return false; }
    }

    // ── Private helper ──────────────────────────────────────────────

    /// <summary>
    /// Extracts the JSON array ([...]) from process stdout text.
    /// Falls back to the full trimmed text if no JSON brackets are found.
    /// </summary>
    private static string ExtractJsonArray(string text)
    {
        var trimmed = text.Trim();
        var jsonStart = trimmed.LastIndexOf('[');
        var jsonEnd = trimmed.LastIndexOf(']');
        return jsonStart >= 0 && jsonEnd > jsonStart
            ? trimmed[jsonStart..(jsonEnd + 1)]
            : trimmed;
    }
}

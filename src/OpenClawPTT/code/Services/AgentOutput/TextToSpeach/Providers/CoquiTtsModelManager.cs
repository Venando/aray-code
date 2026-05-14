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
            host.AddMessage("[red]    ✗ uv is not installed — cannot fetch model list.[/]");
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
            host.AddMessage("[red]    ✗ Live model fetch timed out after 5 minutes.[/]");
            host.AddMessage("[grey]      Check network and uv/Python setup, then re-run /reconfigure TTS.[/]");
        }
        catch (Exception ex)
        {
            var errorMsg = ex.Message;
            host.AddMessage("[red]    ✗ Live model fetch failed. Error from uv:[/]");

            var errorLines = errorMsg.Split('\n');
            var shown = 0;
            foreach (var line in errorLines)
            {
                if (shown >= 20) { host.AddMessage("[red]      ... (output truncated)[/]"); break; }
                var trimmedLine = line.Trim();
                if (!string.IsNullOrWhiteSpace(trimmedLine))
                {
                    host.AddMessage($"[red]      {EscapeSpectreMarkup(trimmedLine)}[/]");
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
        var projectDir = Path.Combine(
            dataDir ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".openclaw-ptt"),
            "coqui-tts-env");

        var env = new CoquiUvEnvironment(dataDir, "tts_models/en/ljspeech/vits", null, null, null);
        env.EnsureProjectFiles();
        CoquiUvEnvironment.EnsureVenvPythonMatches(projectDir);

        var uvPath = CoquiUvEnvironment.FindUv() ?? "uv";
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

        var pythonInfo = CoquiUvEnvironment.ValidatedPythonPath != null
            ? $" (Python: {CoquiUvEnvironment.ValidatedPythonPath})"
            : "";
        host.AddMessage($"[grey]    Fetching model list from coqui/TTS on HuggingFace{pythonInfo}...[/]");
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

            var liveList = modelNames
            .Select(name => CoquiTtsModelInfo.FromModelName(name))
            .OrderBy(m => m.Name)
            .ToList();

            if (CoquiUvEnvironment.IsUvBuildBroken)
                CoquiUvEnvironment.ClearBrokenFlagKeepPython();

            return liveList;
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

    private static string EscapeSpectreMarkup(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Replace("[", "[[").Replace("]", "]]");
    }

    /// <summary>
    /// Returns model names that are currently cached on disk by running
    /// a Python command that walks the HuggingFace cache directly.
    /// Much more reliable than C# filesystem traversal.
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetCachedModelsAsync(
        IStreamShellHost host, string? dataDir, CancellationToken ct = default)
    {
        var projectDir = Path.Combine(
            dataDir ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".openclaw-ptt"),
            "coqui-tts-env");

        if (!CoquiUvEnvironment.IsUvAvailable())
        {
            host.AddMessage("[grey]    [[GetCachedModels]] uv not available[/]");
            return Array.Empty<string>();
        }

        host.AddMessage($"[grey]    [[GetCachedModels]] Running Python cache scan via list_cached.py...[/]");

        // Write the Python script to a temp file (avoids -c escaping/syntax issues)
        var scriptPath = Path.Combine(projectDir, "list_cached.py");
        File.WriteAllText(scriptPath, CoquiUvEnvironment.ListCachedModelPathsScript());

        var uvPath = CoquiUvEnvironment.FindUv() ?? "uv";

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
                host.AddMessage("[red]    [[GetCachedModels]] Process.Start returned null[/]");
                return Array.Empty<string>();
            }

            var stdout = await process.StandardOutput.ReadToEndAsync(linkedCts.Token);
            var stderr = await process.StandardError.ReadToEndAsync(linkedCts.Token);
            await process.WaitForExitAsync(linkedCts.Token);

            host.AddMessage($"[grey]    [[GetCachedModels]] exit={process.ExitCode}, stdout_len={stdout.Length}, stderr_len={stderr.Length}[/]");

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                foreach (var line in stderr.Split('\n'))
                {
                    var t = line.Trim();
                    if (!string.IsNullOrEmpty(t))
                        host.AddMessage($"[grey]    [GetCachedModels:stderr] {EscapeSpectreMarkup(t[..Math.Min(t.Length, 120)])}[/]");
                }
            }

            if (process.ExitCode != 0)
            {
                host.AddMessage($"[red]    [[GetCachedModels]] non-zero exit code, stderr excerpt: {EscapeSpectreMarkup(stderr.Trim()[..Math.Min(stderr.Trim().Length, 200)])}[/]");
                return Array.Empty<string>();
            }

            var trimmed = stdout.Trim();
            host.AddMessage($"[grey]    [[GetCachedModels]] stdout trimmed: '{EscapeSpectreMarkup(trimmed[..Math.Min(trimmed.Length, 500)])}'[/]");

            if (string.IsNullOrEmpty(trimmed))
            {
                host.AddMessage("[yellow]    [[GetCachedModels]] stdout empty after trim[/]");
                return Array.Empty<string>();
            }

            var jsonStart = trimmed.LastIndexOf('[');
            var jsonEnd = trimmed.LastIndexOf(']');
            var jsonText = (jsonStart >= 0 && jsonEnd > jsonStart)
                ? trimmed[jsonStart..(jsonEnd + 1)]
                : trimmed;

            host.AddMessage($"[grey]    [[GetCachedModels]] json: '{EscapeSpectreMarkup(jsonText[..Math.Min(jsonText.Length, 300)])}'[/]");

            var names = JsonSerializer.Deserialize<List<string>>(jsonText);
            if (names == null)
            {
                host.AddMessage("[yellow]    [[GetCachedModels]] Deserialize returned null[/]");
                return Array.Empty<string>();
            }

            host.AddMessage($"[green]    [[GetCachedModels]] Found {names.Count} cached models[/]");

            // Successful uv run proves the environment works — clear any stale
            // broken flag set by a previous TTS service startup failure.
            if (CoquiUvEnvironment.IsUvBuildBroken)
                CoquiUvEnvironment.ClearBrokenFlagKeepPython();

            return names;
        }
        catch (Exception ex)
        {
            host.AddMessage($"[red]    [[GetCachedModels]] Exception: {EscapeSpectreMarkup(ex.Message)}[/]");
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

    private static string SummarizeBuildError(string errorText)
    {
        if (string.IsNullOrWhiteSpace(errorText))
            return "Build failed (no details)";

        var lines = errorText.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Contains("requires python", StringComparison.OrdinalIgnoreCase))
                return trimmed;
            if (trimmed.Contains("RuntimeError", StringComparison.Ordinal))
                return trimmed;
            if (trimmed.Contains("Failed to build", StringComparison.Ordinal))
                return trimmed;
            if (trimmed.Contains("TypeError", StringComparison.Ordinal))
                return trimmed;
        }

        var first = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
        return first?.Trim() ?? "Build failed";
    }

    private static bool IsBuildError(string errorText)
    {
        if (string.IsNullOrWhiteSpace(errorText)) return false;
        return errorText.Contains("Failed to build", StringComparison.Ordinal)
            || errorText.Contains("build_wheel", StringComparison.Ordinal)
            || errorText.Contains("build backend", StringComparison.Ordinal)
            || errorText.Contains("RuntimeError", StringComparison.Ordinal);
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
            _host.AddMessage($"[red]    Cannot download — uv environment is broken: {EscapeSpectreMarkup(detail)}[/]");
            throw new InvalidOperationException($"uv environment is broken: {detail}");
        }

        _host.AddMessage($"[grey]    Starting download of {modelName}...[/]");
        progressCallback?.Invoke(modelName, "Starting download (uv resolving packages)...", null, null, false);

        var pythonCmd = CoquiUvEnvironment.BuildPreDownloadCommand(modelName);
        var uvPath = CoquiUvEnvironment.FindUv() ?? "uv";
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
                        _host.AddMessage($"[grey]      {EscapeSpectreMarkup(line)}[/]");
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
                var summary = SummarizeBuildError(errorDetail);

                if (IsBuildError(errorDetail))
                    CoquiUvEnvironment.MarkUvBuildBroken(summary);

                _host.AddMessage($"[red]    Download failed (exit={process.ExitCode}): {EscapeSpectreMarkup(summary)}[/]");
                progressCallback?.Invoke(modelName, $"Failed (exit={process.ExitCode})", null, null, false);
                throw new InvalidOperationException($"Coqui TTS download failed (exit={process.ExitCode}): {summary}");
            }

            var okInStdout = stdoutText.Contains("OK", StringComparison.Ordinal);
            var isCached = okInStdout || IsModelCached(modelName);
            if (isCached)
            {
                // Some Coqui models (e.g. jenny) are distributed as ZIP archives.
                // Extract them so TTS can find the model files at the expected level.
                var extracted = ExtractModelZips(modelName);
                if (extracted > 0)
                    _host.AddMessage($"[green]    ✓ Model {modelName} cached (extracted {extracted} archive(s)).[/]");
                else
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

    /// <summary>
    /// Returns the Coqui TTS model directory path, or null if not found.
    /// </summary>
    private static string? GetModelDir(string modelName)
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

    /// <summary>
    /// Extracts any .zip archives found in the model directory.
    /// Some Coqui models (e.g. jenny) are distributed as ZIPs that need
    /// unpacking before TTS can load them. Deletes the ZIP after extraction.
    /// Returns the number of archives extracted.
    /// </summary>
    internal static int ExtractModelZips(string modelName)
    {
        var modelDir = GetModelDir(modelName);
        if (modelDir == null)
            return 0;

        var extracted = 0;
        foreach (var zipPath in Directory.EnumerateFiles(modelDir, "*.zip"))
        {
            try
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, modelDir, overwriteFiles: true);
                File.Delete(zipPath);
                extracted++;
            }
            catch
            {
                // Extraction failed — leave the ZIP, model may still work
            }
        }
        return extracted;
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

    public static CoquiTtsModelInfo FromModelName(string modelName)
    {
        var parts = modelName.Split('/');
        if (parts.Length < 3)
            return new CoquiTtsModelInfo(modelName, modelName);

        var lang = parts.Length > 1 ? parts[1] : "";
        var dataset = parts.Length > 2 ? parts[2] : "";
        var arch = parts.Length > 3 ? parts[3] : "";

        var desc = string.Join(" · ", new[] { lang, dataset, arch }
            .Where(s => !string.IsNullOrEmpty(s)));
        return new CoquiTtsModelInfo(modelName, desc);
    }
}

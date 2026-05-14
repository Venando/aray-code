using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenClawPTT.TTS.Providers;

/// <summary>
/// Manages the <c>uv</c> + <c>Coqui TTS</c> Python environment.
///
/// <para>
/// Because Coqui TTS models take 10-30s to load, we use a long-running
/// subprocess with a JSON stdin/stdout protocol. <c>uv</c> handles:
/// <list type="bullet">
///   <item>Python download and version management</item>
///   <item>Package dependencies (TTS, torch, etc.) via pyproject.toml</item>
///   <item>No PythonPath config — <c>uv run</c> resolves everything</item>
/// </list>
/// </para>
///
/// <para>
/// Project layout under <c>~/.openclaw-ptt/coqui-tts-env/</c>:
/// <c>pyproject.toml</c> — declares TTS + torch dependencies;
/// <c>tts_service.py</c> — embedded long-running service script
/// (content defined in <see cref="CoquiTtsScripts"/>).
/// </para>
/// </summary>
public sealed class CoquiUvEnvironment
{
    private readonly string _projectDir;
    private readonly string _modelName;
    private readonly string? _modelPath;
    private readonly string? _ttsConfigPath;
    private readonly string? _espeakNgPath;

    // Lazy: resolved once
    private static string? s_uvPath;
    private static readonly object s_uvLock = new();
    private static bool s_scriptsExtracted;
    private static readonly object s_scriptsLock = new();

    /// <summary>
    /// Set after the first <c>uv run</c> attempt fails with a build/dependency error.
    /// Prevents retrying the same doomed operation (e.g. Python version mismatch).
    /// </summary>
    public static bool IsUvBuildBroken { get; private set; }

    /// <summary>Human-readable reason why the build is broken, for display.</summary>
    public static string? UvBuildErrorDetail { get; private set; }

    /// <summary>
    /// Path to the validated Python interpreter (e.g. C:\...\python3.11.exe).
    /// Set by <see cref="ValidatePythonVersionAsync"/> on success.
    /// Passed to <c>uv run --python</c> to guarantee the right interpreter.
    /// </summary>
    public static string? ValidatedPythonPath { get; private set; }

    /// <summary>Marks the uv environment as broken so retries are skipped.</summary>
    public static void MarkUvBuildBroken(string detail)
    {
        IsUvBuildBroken = true;
        UvBuildErrorDetail = detail;
    }

    /// <summary>
    /// Clears only the broken flag and error detail, without wiping
    /// <see cref="ValidatedPythonPath"/>. Use when a successful <c>uv run</c>
    /// operation proved the environment works with the currently pinned Python.
    /// </summary>
    public static void ClearBrokenFlagKeepPython()
    {
        IsUvBuildBroken = false;
        UvBuildErrorDetail = null;
    }

    /// <summary>Resets the broken flag — call after user fixes Python/uv.</summary>
    public static void ResetBrokenFlag()
    {
        IsUvBuildBroken = false;
        UvBuildErrorDetail = null;
        ValidatedPythonPath = null;
    }

    /// <summary>
    /// Checks whether the error text indicates a uv build/dependency failure.
    /// Centralized here so both <see cref="CoquiTtsModelManager"/> and
    /// <see cref="ConfigWizard.CoquiTtsConfigFlow"/> share the same patterns.
    /// </summary>
    public static bool IsBuildError(string? errorText)
    {
        if (string.IsNullOrWhiteSpace(errorText)) return false;
        return errorText.Contains("Failed to build", StringComparison.Ordinal)
            || errorText.Contains("build_wheel", StringComparison.Ordinal)
            || errorText.Contains("build backend", StringComparison.Ordinal)
            || errorText.Contains("RuntimeError", StringComparison.Ordinal);
    }

    /// <summary>
    /// Extracts a concise, human-readable summary from a uv build error.
    /// Centralized to avoid duplication with <see cref="CoquiTtsModelManager"/>.
    /// </summary>
    public static string SummarizeBuildError(string errorText)
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

    public CoquiUvEnvironment(string? dataDir, string modelName, string? modelPath, string? ttsConfigPath, string? espeakNgPath)
    {
        _projectDir = Path.Combine(
            dataDir ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".openclaw-ptt"),
            "coqui-tts-env");
        _modelName = modelName;
        _modelPath = modelPath;
        _ttsConfigPath = ttsConfigPath;
        _espeakNgPath = espeakNgPath;
        Directory.CreateDirectory(_projectDir);
    }

    public string ProjectDir => _projectDir;

    // ── uv discovery ────────────────────────────────────────────────

    public static string? FindUv()
    {
        lock (s_uvLock)
        {
            if (s_uvPath != null) return s_uvPath;

            var name = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "uv.exe" : "uv";
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";

            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                var fp = Path.Combine(dir, name);
                if (File.Exists(fp)) { s_uvPath = fp; return fp; }
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var common = new[] {
                Path.Combine(home, ".local", "bin", name),
                Path.Combine(home, ".cargo", "bin", name),
                $"/usr/local/bin/{name}",
            };
            foreach (var p in common)
            {
                if (File.Exists(p)) { s_uvPath = p; return p; }
            }
            return null;
        }
    }

    public static string GetInstallInstructions() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "powershell -c \"irm https://astral.sh/uv/install.ps1 | iex\""
            : "curl -LsSf https://astral.sh/uv/install.sh | sh";

    public static bool IsUvAvailable() => FindUv() != null;

    /// <summary>
    /// Result from <see cref="ValidatePythonVersionAsync"/>.
    /// </summary>
    public sealed class PythonVersionResult
    {
        /// <summary>Null on success; error message on failure.</summary>
        public string? Error { get; init; }
        /// <summary>Resolved Python path (e.g. C:\Users\...\python3.11.exe).</summary>
        public string? PythonPath { get; init; }
        /// <summary>Human-readable version string (e.g. "3.11.13").</summary>
        public string? PythonVersion { get; init; }
        /// <summary>True when the check succeeded and a compatible Python was found.</summary>
        public bool Ok => Error == null && PythonPath != null;
    }

    /// <summary>
    /// Validates that <c>uv</c> can find a Python in [3.9, 3.12) for Coqui TTS.
    /// Uses <c>uv python list</c> (fast, lists only installed interpreters, no downloads).
    /// Streams progress via <paramref name="onProgress"/> so the user isn't staring at silence.
    /// </summary>
    public static async Task<PythonVersionResult> ValidatePythonVersionAsync(
        string? dataDir,
        Action<string>? onProgress = null,
        CancellationToken ct = default)
    {
        if (!IsUvAvailable())
            return new PythonVersionResult { Error = "uv is not installed" };

        if (IsUvBuildBroken)
            return new PythonVersionResult { Error = UvBuildErrorDetail ?? "uv environment is broken" };

        var uvPath = FindUv()!;

        onProgress?.Invoke("Running uv python list (checking installed interpreters)...");

        // uv python list — fast, only lists installed interpreters, no downloads
        var psi = new ProcessStartInfo
        {
            FileName = uvPath,
            Arguments = "python list --only-installed",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        Process? process = null;
        try
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            process = Process.Start(psi);
            if (process == null)
                return new PythonVersionResult { Error = "Failed to start uv python list" };

            // Read both streams concurrently — show stderr as progress
            var stdoutTask = process.StandardOutput.ReadToEndAsync(linked.Token);
            var stderrTask = Task.Run(async () =>
            {
                var reader = process.StandardError;
                var sb = new StringBuilder();
                while (true)
                {
                    var line = await reader.ReadLineAsync(linked.Token).ConfigureAwait(false);
                    if (line == null) break;
                    var trimmed = line.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                    {
                        onProgress?.Invoke(trimmed);
                        sb.AppendLine(trimmed);
                    }
                }
                return sb.ToString();
            }, linked.Token);

            await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(linked.Token))
                .ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                var stderr = stderrTask.Result.Trim();
                return new PythonVersionResult
                {
                    Error = string.IsNullOrEmpty(stderr)
                        ? $"No compatible Python found. Coqui TTS requires Python >=3.9, <3.12. Install Python 3.11 and restart."
                        : $"Python check failed: {stderr}"
                };
            }

            var stdout = stdoutTask.Result.Trim();
            if (string.IsNullOrEmpty(stdout))
                return new PythonVersionResult
                {
                    Error = "No Python interpreters installed. Install Python >=3.9, <3.12 (e.g. 3.11) and restart."
                };

            // Parse uv python list output — each line is: cpython-3.11.13-linux-x86_64-gnu    /path/to/python3.11
            var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;

                var versionStr = ExtractPythonVersion(parts[0]);
                if (versionStr == null) continue;

                if (!Version.TryParse(versionStr, out var ver)) continue;

                // Must be in [3.9, 3.12)
                if (ver.Major != 3 || ver.Minor < 9 || ver.Minor >= 12)
                    continue;

                // Found a compatible Python!
                var pythonPath = parts[1];
                onProgress?.Invoke($"Found compatible Python: {versionStr} at {pythonPath}");
                ValidatedPythonPath = pythonPath;
                return new PythonVersionResult
                {
                    PythonPath = pythonPath,
                    PythonVersion = versionStr
                };
            }

            // No compatible Python found — list what IS installed for clarity
            var installed = lines
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();
            var detail = installed.Count > 0
                ? $"Installed: {string.Join(", ", installed.Select(l => l.Split([' ', '\t'])[0]))}. Need Python >=3.9, <3.12."
                : "No Python interpreters installed. Need Python >=3.9, <3.12 (e.g. 3.11).";
            return new PythonVersionResult { Error = detail };
        }
        catch (OperationCanceledException)
        {
            return new PythonVersionResult { Error = "Python version check timed out" };
        }
        catch (Exception ex)
        {
            return new PythonVersionResult { Error = $"Python version check failed: {ex.Message}" };
        }
        finally
        {
            // Always kill the process — don't leave orphaned uv/python processes
            if (process != null)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch { /* already exited */ }
                process.Dispose();
            }
        }
    }

    /// <summary>
    /// Extracts a Python version string like "3.11.13" from a key like
    /// "cpython-3.11.13-linux-x86_64-gnu".
    /// </summary>
    private static string? ExtractPythonVersion(string text)
    {
        // Match "cpython-X.Y.Z" or "X.Y.Z" pattern
        var match = System.Text.RegularExpressions.Regex.Match(
            text, @"(\d+\.\d+(\.\d+)?)");
        return match.Success ? match.Groups[1].Value : null;
    }

    // ── Project files ───────────────────────────────────────────────

    public void EnsureProjectFiles()
    {
        lock (s_scriptsLock)
        {
            // Always write the latest pyproject.toml (may have build-dep fixes)
            var pyprojectPath = Path.Combine(_projectDir, "pyproject.toml");
            File.WriteAllText(pyprojectPath, CoquiTtsScripts.PyProjectToml, Encoding.UTF8);

            if (s_scriptsExtracted) return;

            var scriptPath = Path.Combine(_projectDir, "tts_service.py");
            File.WriteAllText(scriptPath, CoquiTtsScripts.TtsServiceScript, Encoding.UTF8);

            s_scriptsExtracted = true;
        }
    }

    /// <summary>
    /// Returns " --python \"<path>\"" when a validated Python is known,
    /// otherwise empty string. Use in all <c>uv run</c> arguments.
    /// </summary>
    public static string GetPythonArg()
    {
        if (!string.IsNullOrEmpty(ValidatedPythonPath))
            return $" --python \"{ValidatedPythonPath}\"";
        return "";
    }

    /// <summary>
    /// If the <c>.venv</c> exists but was created with a different Python than
    /// the validated one, delete it so <c>uv run --python</c> recreates it correctly.
    /// </summary>
    public static void EnsureVenvPythonMatches(string projectDir)
    {
        if (string.IsNullOrEmpty(ValidatedPythonPath))
            return;

        var pyvenvCfg = Path.Combine(projectDir, ".venv", "pyvenv.cfg");
        if (!File.Exists(pyvenvCfg))
            return;

        try
        {
            var lines = File.ReadAllLines(pyvenvCfg);
            foreach (var line in lines)
            {
                if (line.StartsWith("home =", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("home=", StringComparison.OrdinalIgnoreCase))
                {
                    var homePath = line[(line.IndexOf('=') + 1)..].Trim();
                    // Normalize paths for comparison
                    if (!string.Equals(
                        Path.GetFullPath(homePath).TrimEnd(Path.DirectorySeparatorChar),
                        Path.GetDirectoryName(Path.GetFullPath(ValidatedPythonPath))?.TrimEnd(Path.DirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase))
                    {
                        // Wrong Python — delete venv so uv recreates it
                        var venvDir = Path.Combine(projectDir, ".venv");
                        Directory.Delete(venvDir, recursive: true);
                    }
                    break;
                }
            }
        }
        catch
        {
            // If anything goes wrong reading/deleting, don't crash — uv will handle it
        }
    }

    // ── Process creation ────────────────────────────────────────────

    /// <summary>
    /// Creates a ProcessStartInfo for <c>uv run python tts_service.py</c>
    /// with model environment variables.
    /// </summary>
    public ProcessStartInfo CreateProcessStartInfo()
    {
        EnsureProjectFiles();
        var uvPath = FindUv() ?? "uv";

        var psi = new ProcessStartInfo
        {
            FileName = uvPath,
            Arguments = $"run{GetPythonArg()} --directory \"{_projectDir}\" python \"{Path.Combine(_projectDir, "tts_service.py")}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        // Pass model config via environment variables (same protocol as legacy)
        if (!string.IsNullOrEmpty(_espeakNgPath))
            psi.Environment["PATH"] = _espeakNgPath + Path.PathSeparator + psi.Environment["PATH"];
        if (!string.IsNullOrEmpty(_modelName))
            psi.Environment["TTS_MODEL"] = _modelName;
        if (!string.IsNullOrEmpty(_modelPath))
            psi.Environment["TTS_MODEL_PATH"] = _modelPath;
        if (!string.IsNullOrEmpty(_ttsConfigPath))
            psi.Environment["TTS_CONFIG_PATH"] = _ttsConfigPath;

        return psi;
    }

    /// <summary>
    /// Builds a uv run Python command to pre-download a Coqui TTS model.
    /// Instantiating TTS(model_name) triggers HuggingFace download.
    /// </summary>
    public static string BuildPreDownloadCommand(string modelName)
    {
        var escaped = modelName.Replace("\\", "\\\\").Replace("\"", "\\\"");
        // PyTorch >=2.6 defaults weights_only=True in torch.load, but Coqui TTS
        // models use legacy checkpoints that need weights_only=False.
        // Monkey-patch torch.load before importing TTS.
        return "import torch, functools; " +
               "torch.load = functools.partial(torch.load, weights_only=False); " +
               "from TTS.api import TTS; " +
               $"m = TTS(model_name=\"{escaped}\", progress_bar=False, gpu=False); " +
               "del m; print('OK')";
    }

    /// <summary>
    /// Returns the content for the <c>list_cached.py</c> Python helper script.
    /// See <see cref="CoquiTtsScripts.ListCachedModelPathsScript"/>.
    /// </summary>
    public static string ListCachedModelPathsScript() => CoquiTtsScripts.ListCachedModelPathsScript();

    /// <summary>
    /// Builds a uv run command to list locally cached Coqui TTS model repos.
    /// </summary>
    public static string BuildListCachedCommand()
    {
        return "import os, json, glob; " +
               "cache = os.path.expanduser('~/.cache/huggingface/hub'); " +
               "models = []; " +
               "for d in glob.glob(cache + '/models--*'): " +
               "    name = d.split('models--')[1]; " +
               "    if 'TTS' in name or 'tts_models' in name or 'coqui' in name.lower(): " +
               "        snap = os.path.join(d, 'snapshots'); " +
               "        if os.path.isdir(snap) and any(os.listdir(snap)): " +
               "            models.append(name.replace('--', '/')); " +
               "print(json.dumps(models))";
    }
}

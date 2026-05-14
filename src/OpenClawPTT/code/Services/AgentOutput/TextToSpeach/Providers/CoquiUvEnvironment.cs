using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenClawPTT.TTS.Providers;

/// <summary>
/// Manages the <c>uv</c> + <c>Coqui TTS</c> Python environment.
///
/// <para>
/// Because Coqui TTS models take 10-30s to load, we use a long-running
/// subprocess with a JSON stdin/stdout protocol (identical to the legacy
/// <c>PythonTtsProvider</c>). Unlike the legacy approach, <c>uv</c> handles:
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
/// <c>tts_service.py</c> — embedded long-running service script.
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
            File.WriteAllText(pyprojectPath, PyProjectToml, Encoding.UTF8);

            if (s_scriptsExtracted) return;

            var scriptPath = Path.Combine(_projectDir, "tts_service.py");
            File.WriteAllText(scriptPath, TtsServiceScript, Encoding.UTF8);

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
    /// Builds a uv run command to pre-download a Coqui TTS model.
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
    /// Returns the content for a Python helper script that lists cached model paths.
    /// Saved to <c>list_cached.py</c> in the project dir and executed via
    /// <c>uv run python list_cached.py</c>.
    /// </summary>
    public static string ListCachedModelPathsScript() => """
import os, json, glob
seen = set()
cache = os.path.expanduser('~/.cache/huggingface/hub')
for repo in glob.glob(cache + '/models--*'):
    base = os.path.basename(repo)
    if not any(k in base.lower() for k in ('tts','coqui','tts_models')):
        continue
    snaps = os.path.join(repo, 'snapshots')
    if not os.path.isdir(snaps):
        continue
    for snap in os.listdir(snaps):
        root = os.path.join(snaps, snap)
        if not os.path.isdir(root):
            continue
        for sub in ['tts_models','vocoder_models']:
            sub_path = os.path.join(root, sub)
            if os.path.isdir(sub_path):
                for lang in os.listdir(sub_path):
                    lang_path = os.path.join(sub_path, lang)
                    if not os.path.isdir(lang_path):
                        continue
                    for ds in os.listdir(lang_path):
                        ds_path = os.path.join(lang_path, ds)
                        if not os.path.isdir(ds_path):
                            continue
                        for arch in os.listdir(ds_path):
                            arch_path = os.path.join(ds_path, arch)
                            if os.path.isdir(arch_path):
                                seen.add(f'{sub}/{lang}/{ds}/{arch}')
print(json.dumps(sorted(seen)))
""" + "\n";

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

    // ── Embedded resources ──────────────────────────────────────────

    private const string PyProjectToml = """
[project]
name = "openclaw-ptt-tts"
version = "0.1.0"
requires-python = ">=3.9,<3.12"
dependencies = [
    "TTS>=0.22.0",
    "torch>=2.0.0",
]

[tool.uv]
# uv downloads the right Python automatically if not installed

[tool.uv.extra-build-dependencies]
# pandas 1.5.3 and tts 0.22.0 (dep of TTS) use pkg_resources at build time
# but don't declare setuptools as a build dependency. setuptools>=67.3
# removed pkg_resources, so we pin to an older version that includes it.
pandas = ["setuptools<67"]
tts = ["setuptools<67"]
""";

    private const string TtsServiceScript = """
#!/usr/bin/env python3
# Coqui TTS long-running service via uv.
# Reads JSON lines from stdin: {"text":"...","id":"...","voice":null|"path.wav"}
# Outputs JSON to stdout: {"type":"ready|ok|error","id":"...","path":"..."}
# Sends "EXIT" to stop.

import sys, os, json, tempfile, time, logging

# ── UTF-8 stdout ──
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

# ── Logging to stderr ──
logging.basicConfig(level=logging.INFO, format="%(message)s", stream=sys.stderr)
log = logging.getLogger("coqui_uv_tts")

# ── Protocol helpers ──
def send(msg):
    print(json.dumps(msg), flush=True)

def protocol(msg_type, **kwargs):
    send({"type": msg_type, **kwargs})

# ── Load model ──
def load_model():
    model_name = os.environ.get("TTS_MODEL")
    model_path = os.environ.get("TTS_MODEL_PATH")
    config_path = os.environ.get("TTS_CONFIG_PATH")

    if not model_name and not model_path:
        raise RuntimeError("No TTS_MODEL or TTS_MODEL_PATH set")

    import torch, functools
    # PyTorch >=2.6 defaults weights_only=True; Coqui TTS models need False
    torch.load = functools.partial(torch.load, weights_only=False)
    from TTS.api import TTS

    device = "cuda" if torch.cuda.is_available() else "cpu"
    log.info("Loading Coqui TTS on %s...", device)

    t0 = time.monotonic()

    if model_path and config_path:
        if not os.path.isfile(model_path):
            raise ValueError(f"TTS_MODEL_PATH is not a file: {model_path!r}")
        if not os.path.isfile(config_path):
            raise ValueError(f"TTS_CONFIG_PATH is not a file: {config_path!r}")
        tts = TTS(model_path=model_path, config_path=config_path, progress_bar=False).to(device)
    else:
        tts = TTS(model_name=model_name, progress_bar=False).to(device)

    elapsed = time.monotonic() - t0
    log.info("Coqui TTS loaded on %s in %.1fs", device, elapsed)

    # Fix for TTS 0.22.0 compat
    if tts.config is not None and not hasattr(tts.config, "languages"):
        object.__setattr__(tts.config, "languages", [])
    if tts.config is not None and not hasattr(tts, "is_multi_lingual"):
        object.__setattr__(tts, "is_multi_lingual", False)

    return tts

# ── Main ──
def main():
    try:
        tts = load_model()
        protocol("ready")
    except Exception as e:
        log.exception("Failed to load Coqui TTS model")
        protocol("error", id="startup", msg=str(e))
        sys.exit(1)

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        if line == "EXIT":
            break

        try:
            req = json.loads(line)
        except json.JSONDecodeError:
            protocol("error", id="unknown", msg=f"Invalid JSON: {line[:100]}")
            continue

        req_id = req.get("id", "unknown")
        text = req.get("text")
        voice = req.get("voice")  # speaker_wav path
        # model switch not supported at runtime — TTS would need reload

        if not text:
            protocol("error", id=req_id, msg="Missing required field: text")
            continue

        try:
            with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as f:
                out_path = f.name

            t0 = time.monotonic()
            if voice:
                tts.tts_to_file(text=text, file_path=out_path, speaker_wav=voice)
            else:
                tts.tts_to_file(text=text, file_path=out_path)

            elapsed = time.monotonic() - t0
            file_size = os.path.getsize(out_path)
            log.info(json.dumps({"perf": True, "id": req_id, "bytes": file_size, "time": round(elapsed, 2)}))

            protocol("ok", id=req_id, path=out_path)
        except Exception:
            log.exception("Request %s failed", req_id)
            protocol("error", id=req_id, msg="TTS synthesis failed")

if __name__ == "__main__":
    main()
""";
}

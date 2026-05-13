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
            Arguments = $"run --directory \"{_projectDir}\" python \"{Path.Combine(_projectDir, "tts_service.py")}\"",
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
        return "import torch; from TTS.api import TTS; " +
               $"m = TTS(model_name=\"{escaped}\", progress_bar=False, gpu=False); " +
               "del m; print('OK')";
    }

    /// <summary>
    /// Builds a uv run command to list locally cached Coqui TTS models.
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
requires-python = ">=3.9"
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

    import torch
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

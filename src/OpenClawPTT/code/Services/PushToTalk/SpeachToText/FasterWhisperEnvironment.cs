using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenClawPTT.Transcriber;

/// <summary>
/// Manages the <c>uv</c> + <c>faster-whisper</c> Python environment.
///
/// <para>
/// Unlike the legacy approach (user-installed Python, fragile PATH/packages),
/// <c>uv</c> provides:
/// <list type="bullet">
///   <item>Automatic Python download and version management</item>
///   <item>Isolated venv with pinned dependencies (pyproject.toml)</item>
///   <item>No PythonPath config — <c>uv run</c> resolves everything</item>
/// </list>
/// </para>
///
/// <para>
/// Project layout under <c>~/.openclaw-ptt/faster-whisper-env/</c>:
/// <c>pyproject.toml</c> — declares <c>faster-whisper</c> dependency.
/// No separate .py script files; inline <c>python -c</c> commands are used.
/// </para>
/// </summary>
public sealed class FasterWhisperEnvironment
{
    private readonly string _projectDir;

    // Lazy: resolved once
    private static string? s_uvPath;
    private static readonly object s_uvLock = new();

    /// <summary>Default project directory under the user data folder.</summary>
    public static string DefaultProjectDir(string? dataDir = null)
    {
        var baseDir = dataDir
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".openclaw-ptt");
        return Path.Combine(baseDir, "faster-whisper-env");
    }

    public FasterWhisperEnvironment(string? dataDir = null)
    {
        _projectDir = DefaultProjectDir(dataDir);
        Directory.CreateDirectory(_projectDir);
    }

    /// <summary>Project directory containing pyproject.toml.</summary>
    public string ProjectDir => _projectDir;

    // ── uv discovery ────────────────────────────────────────────────

    /// <summary>
    /// Finds <c>uv</c> on PATH, or returns null if not installed.
    /// </summary>
    public static string? FindUv()
    {
        lock (s_uvLock)
        {
            if (s_uvPath != null)
                return s_uvPath;

            var name = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "uv.exe" : "uv";
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";

            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                var fullPath = Path.Combine(dir, name);
                if (File.Exists(fullPath))
                {
                    s_uvPath = fullPath;
                    return fullPath;
                }
            }

            // Common install locations
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var commonPaths = new[]
            {
                Path.Combine(home, ".local", "bin", name),
                Path.Combine(home, ".cargo", "bin", name),
                $"/usr/local/bin/{name}",
            };

            foreach (var p in commonPaths)
            {
                if (File.Exists(p))
                {
                    s_uvPath = p;
                    return p;
                }
            }

            return null;
        }
    }

    /// <summary>Shell-specific install instructions for <c>uv</c>.</summary>
    public static string GetInstallInstructions()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return "powershell -c \"irm https://astral.sh/uv/install.ps1 | iex\"";
        return "curl -LsSf https://astral.sh/uv/install.sh | sh";
    }

    /// <summary>Whether <c>uv</c> is available.</summary>
    public static bool IsUvAvailable() => FindUv() != null;

    // ── Project setup ───────────────────────────────────────────────

    /// <summary>
    /// Writes <c>pyproject.toml</c> to the project directory if it doesn't exist.
    /// Declares <c>faster-whisper</c> as a dependency — <c>uv</c> handles
    /// Python download, venv creation, and package installation on first <c>uv run</c>.
    /// </summary>
    public void EnsureProjectFiles()
    {
        // Always write latest pyproject.toml (may have dependency fixes)
        var pyprojectPath = Path.Combine(_projectDir, "pyproject.toml");
        File.WriteAllText(pyprojectPath, PyProjectToml, Encoding.UTF8);
    }

    // ── Process creation ────────────────────────────────────────────

    /// <summary>
    /// Creates a ProcessStartInfo for <c>uv run python -c "&lt;command&gt;"</c>.
    /// The project directory is set so <c>uv</c> resolves dependencies from pyproject.toml.
    /// </summary>
    public ProcessStartInfo CreateProcessStartInfo(string pythonCommand)
    {
        EnsureProjectFiles();

        var uvPath = FindUv() ?? "uv";

        return new ProcessStartInfo
        {
            FileName = uvPath,
            Arguments = $"run --directory \"{_projectDir}\" python -c \"{pythonCommand.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
    }

    // ── Command builders (inline Python) ────────────────────────────

    /// <summary>
    /// Builds a Python one-liner that loads faster-whisper, transcribes a WAV file,
    /// and prints the transcribed text to stdout.
    /// Uses single quotes for Python strings to avoid escaping conflicts.
    /// </summary>
    public static string BuildTranscribeCommand(string modelName, string wavPath)
    {
        // Escape backslashes and single quotes for the Python string literal
        var escapedWav = wavPath.Replace("\\", "\\\\").Replace("'", "\\'");
        var escapedModel = modelName.Replace("\\", "\\\\").Replace("'", "\\'");

        return $"from faster_whisper import WhisperModel; " +
               $"m = WhisperModel('{escapedModel}', device='cpu', compute_type='int8'); " +
               $"segs, info = m.transcribe('{escapedWav}'); " +
               $"text = ' '.join(s.text for s in segs); " +
               $"print(text)";
    }

    /// <summary>
    /// Builds a Python one-liner that triggers model download by instantiating
    /// WhisperModel. The CTranslate2 model is auto-downloaded to the HuggingFace cache.
    /// </summary>
    public static string BuildPreDownloadCommand(string modelName)
    {
        var escapedModel = modelName.Replace("\\", "\\\\").Replace("'", "\\'");
        return $"from faster_whisper import WhisperModel; " +
               $"m = WhisperModel('{escapedModel}', device='cpu', compute_type='int8'); " +
               $"del m; print('OK')";
    }

    /// <summary>
    /// Builds a Python one-liner that lists locally cached faster-whisper models
    /// by checking the HuggingFace cache. Outputs one model name per line.
    /// </summary>
    public static string BuildListCachedModelsCommand()
    {
        return "import os, glob; " +
               "cache = os.path.expanduser('~/.cache/huggingface/hub'); " +
               "if not os.path.isdir(cache): print(''); exit(0); " +
               "models = set(); " +
               "for d in glob.glob(cache + '/models--*'): " +
               "    name = d.split('models--')[1].replace('--', '/'); " +
               "    if 'faster-whisper' in name.lower() or 'whisper' in name.lower() or 'Systran' in name: " +
               "        models.add(name); " +
               "for m in sorted(models): print(m)";
    }

    // ── pyproject.toml template ─────────────────────────────────────

    private const string PyProjectToml = """
[project]
name = "openclaw-ptt-stt"
version = "0.1.0"
requires-python = ">=3.9"
dependencies = [
    "faster-whisper>=1.0.0",
]

[tool.uv]
# uv downloads the right Python automatically if not installed
""";
}

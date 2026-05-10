using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace OpenClawPTT.TTS.Providers;

/// <summary>
/// Manages Python environment setup: script extraction, Python resolution, and environment variables.
/// </summary>
public sealed class PythonEnvironment
{
    private readonly string _pythonPath;
    private readonly string _modelName;
    private readonly string? _modelPath;
    private readonly string? _ttsConfigPath;
    private readonly string? _espeakNgPath;

    private static bool s_extracted;
    private static readonly string s_scriptFolder = Path.Combine(Path.GetTempPath(), "openclaw-ptt-tts", "scripts");
    private static readonly string s_mainScript = Path.Combine(s_scriptFolder, "tts_service.py");

    public PythonEnvironment(string pythonPath, string modelName, string? modelPath, string? ttsConfigPath, string? espeakNgPath)
    {
        _pythonPath = pythonPath;
        _modelName = modelName;
        _modelPath = modelPath;
        _ttsConfigPath = ttsConfigPath;
        _espeakNgPath = espeakNgPath;
    }

    /// <summary>
    /// Resolves the Python executable path, checking explicit path first, then PATH environment variable.
    /// </summary>
    public string ResolvePython()
    {
        if (!string.IsNullOrEmpty(_pythonPath))
        {
            var pythonExe = Path.Combine(_pythonPath, "python.exe");
            if (File.Exists(pythonExe))
                return pythonExe;
            pythonExe = Path.Combine(_pythonPath, "Scripts", "python.exe");
            if (File.Exists(pythonExe))
                return pythonExe;
        }

        var envPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in envPath.Split(Path.PathSeparator))
        {
            var exe = Path.Combine(dir, "python.exe");
            if (File.Exists(exe))
                return exe;
        }

        return "python";
    }

    /// <summary>
    /// Gets the script path, extracting embedded scripts if no override is provided.
    /// </summary>
    public string GetScriptPath(string? overridePath)
    {
        if (!string.IsNullOrEmpty(overridePath))
            return overridePath;
        ExtractAllScripts();
        return s_mainScript;
    }

    /// <summary>
    /// Creates a configured ProcessStartInfo for launching the TTS service script.
    /// </summary>
    public ProcessStartInfo CreateProcessStartInfo(string? scriptPathOverride)
    {
        var pythonExe = ResolvePython();
        var psi = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = $"-u \"{GetScriptPath(scriptPathOverride)}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        ApplyEnvironmentVariables(psi);
        return psi;
    }

    /// <summary>
    /// Applies Python environment variables to the process start info.
    /// </summary>
    private void ApplyEnvironmentVariables(ProcessStartInfo psi)
    {
        // Force CPU-only mode — avoids CUDA initialization overhead and potential driver issues
        psi.Environment["CUDA_VISIBLE_DEVICES"] = "";
        if (!string.IsNullOrEmpty(_espeakNgPath))
            psi.Environment["PATH"] = _espeakNgPath + ";" + psi.Environment["PATH"];
        if (!string.IsNullOrEmpty(_modelName))
            psi.Environment["TTS_MODEL"] = _modelName;
        if (!string.IsNullOrEmpty(_modelPath))
            psi.Environment["TTS_MODEL_PATH"] = _modelPath;
        if (!string.IsNullOrEmpty(_ttsConfigPath))
            psi.Environment["TTS_CONFIG_PATH"] = _ttsConfigPath;
    }

    private static void ExtractAllScripts()
    {
        if (s_extracted)
            return;

        var asm = Assembly.GetExecutingAssembly();
        // RootNamespace from csproj is OpenClawPTT — resource names use it, not the assembly name
        var resourcePrefix = "OpenClawPTT.scripts.";
        var allResources = asm.GetManifestResourceNames().ToList();
        var resourceNames = allResources.Where(r => r.StartsWith(resourcePrefix)).ToList();

        foreach (var resourceName in resourceNames)
        {
            string relativePath = resourceName.Substring(resourcePrefix.Length);
            string outputPath = Path.Combine(s_scriptFolder, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            // Skip extraction if the file already exists — avoids IOException when a
            // previous Python process still holds a lock on the file (e.g. zombie process
            // from a crashed app instance left in Task Manager).
            if (File.Exists(outputPath))
                continue;

            try
            {
                using var stream = asm.GetManifestResourceStream(resourceName)!;
                using var file = File.Create(outputPath);
                stream.CopyTo(file);
            }
            catch (IOException)
            {
                // File is locked by another process (zombie Python from a previous run).
                // The script content is already extracted and readable — the running
                // process can still use the locked file via Read sharing. Skip overwrite.
                continue;
            }
        }

        s_extracted = true;
    }
}

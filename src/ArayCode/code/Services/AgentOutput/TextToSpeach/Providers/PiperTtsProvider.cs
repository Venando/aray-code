using System.Diagnostics;

namespace ArayCode.TTS.Providers;

/// <summary>
/// Piper TTS provider (local, fast).
/// https://github.com/rhasspy/piper
/// </summary>
public sealed class PiperTtsProvider : ITextToSpeech
{
    private readonly string _piperPath;
    private readonly string _modelPath;
    private readonly string _defaultVoice;

    public string ProviderName => "Piper TTS";

    public IReadOnlyList<string> AvailableVoices { get; } =
    [
        // These are example voices — actual voices depend on installed models
        "en_US-lessac", "en_US-lessac-medium",
        "en_GB-sue-medium", "en_GB-alba-medium",
        "de_DE-thorsten-medium", "de_DE-kerstin-medium",
        "fr_FR-siwis-medium", "fr_FR-siwis-medium",
    ];

    public IReadOnlyList<string> AvailableModels { get; } = []; // Models are file-based

    public PiperTtsProvider(string piperPath = "piper", string modelPath = "", string voice = "en_US-lessac")
    {
        _piperPath = piperPath;
        _modelPath = modelPath;
        _defaultVoice = voice;
    }

    public async Task<byte[]> SynthesizeAsync(string text, string? voice = null, string? model = null, CancellationToken ct = default)
    {
        var selectedVoice = voice ?? _defaultVoice;
        var modelFile = model ?? FindModelFile(selectedVoice);

        if (!File.Exists(modelFile))
        {
            throw new FileNotFoundException(
                $"Piper model not found: {modelFile}. " +
                "Download models from https://github.com/rhasspy/piper/tree/master/src/pythonFrontend/sample_models.md");
        }

        var tempOutput = TempFileHelper.CreateTempPath("piper_tts", ".wav");

        try
        {
            var psi = BuildProcessStartInfo(modelFile, tempOutput);

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start piper process");

            await process.StandardInput.WriteLineAsync(text);
            process.StandardInput.Close();

            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(ct);
                throw new InvalidOperationException($"Piper TTS failed: {error}");
            }

            if (!File.Exists(tempOutput))
            {
                throw new InvalidOperationException($"Piper TTS did not produce output file: {tempOutput}");
            }

            return await File.ReadAllBytesAsync(tempOutput, ct);
        }
        finally
        {
            TempFileHelper.TryDelete(tempOutput);
        }
    }

    /// <summary>Builds the process start info for a piper synthesis invocation.</summary>
    private ProcessStartInfo BuildProcessStartInfo(string modelFile, string outputFile)
    {
        return new ProcessStartInfo
        {
            FileName = _piperPath,
            Arguments = $"--model_file \"{modelFile}\" --output_file \"{outputFile}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
    }

    /// <summary>
    /// Resolves the model file path for a given voice name.
    /// Checks <c>_modelPath</c> for both <c>.onnx</c> and <c>.onnx.json</c> files.
    /// Falls back to treating the voice name as an explicit path.
    /// </summary>
    private string FindModelFile(string voice)
    {
        var onnxFile = Path.Combine(_modelPath, $"{voice}.onnx");
        if (File.Exists(onnxFile))
            return onnxFile;

        var onnxJsonFile = Path.Combine(_modelPath, $"{voice}.onnx.json");
        if (File.Exists(onnxJsonFile))
            return onnxJsonFile;

        // Default: assume voice is the full path to model file
        return voice;
    }
}

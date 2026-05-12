using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT.Transcriber;

/// <summary>
/// Adapter for local Whisper.cpp transcription.
/// Uses the whisper CLI binary with a downloaded model from WhisperCppModelManager.
/// </summary>
public sealed class WhisperCppTranscriberAdapter : ITranscriber
{
    private readonly string? _whisperBinaryPath;
    private readonly WhisperCppModelManager _modelManager;
    private readonly string _modelName;
    private bool _disposed;

    /// <summary>
    /// Creates a new WhisperCppTranscriberAdapter.
    /// </summary>
    /// <param name="modelManager">Model manager for model lookup and download.</param>
    /// <param name="modelName">Whisper model name (e.g. "base", "small.en").</param>
    /// <param name="whisperBinaryPath">
    /// Path to the whisper CLI binary. If null, auto-detected via PATH.
    /// Falls back to "whisper" if not found.
    /// </param>
    public WhisperCppTranscriberAdapter(
        WhisperCppModelManager modelManager,
        string modelName,
        string? whisperBinaryPath = null)
    {
        _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
        _modelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
        _whisperBinaryPath = whisperBinaryPath ?? WhisperCppModelManager.FindWhisperBinary() ?? "whisper";
    }

    public async Task<string> TranscribeAsync(byte[] wavBytes, string fileName = "audio.wav", CancellationToken ct = default)
    {
        if (wavBytes == null || wavBytes.Length == 0)
            throw new ArgumentNullException(nameof(wavBytes), "WAV bytes must not be null or empty.");

        var modelPath = _modelManager.GetModelPath(_modelName);
        if (!File.Exists(modelPath))
            throw new TranscriberException(
                $"Whisper model '{_modelName}' not found. Please download it first via /reconfigure → Speech-To-Text.");

        // Write to a temp file for whisper CLI to process
        var tempDir = Path.Combine(Path.GetTempPath(), "openclaw-ptt");
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, fileName);

        try
        {
            await File.WriteAllBytesAsync(tempFile, wavBytes, ct).ConfigureAwait(false);

            var psi = new ProcessStartInfo
            {
                FileName = _whisperBinaryPath,
                Arguments = $"--model \"{modelPath}\" --file \"{tempFile}\" --output-txt --no-timestamps",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)
                ?? throw new TranscriberException($"Failed to start whisper process. Binary: {_whisperBinaryPath}");

            var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            var error = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);

            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new TranscriberException($"whisper.cpp exited with code {process.ExitCode}: {error}");
            }

            // Output file will be same name but .txt extension
            var outputFile = Path.ChangeExtension(tempFile, ".txt");
            if (File.Exists(outputFile))
            {
                var result = await File.ReadAllTextAsync(outputFile, ct).ConfigureAwait(false);
                return result.Trim();
            }

            // If no output file, return stdout (some whisper versions output to stdout)
            return output.Trim();
        }
        finally
        {
            // Cleanup temp files
            try
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
                var txtFile = Path.ChangeExtension(tempFile, ".txt");
                if (File.Exists(txtFile))
                    File.Delete(txtFile);
            }
            catch { /* best effort cleanup */ }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}

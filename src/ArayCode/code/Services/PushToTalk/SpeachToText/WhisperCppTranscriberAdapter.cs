using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ArayCode.Transcriber;

/// <summary>
/// Adapter for local Whisper transcription using whisper.cpp (C++).
/// Uses pre-downloaded .bin model files.
/// </summary>
public sealed class WhisperCppTranscriberAdapter : ITranscriber
{
    private readonly string _whisperBinaryPath;
    private readonly WhisperCppModelManager _modelManager;
    private readonly string _modelName;
    private readonly TimeSpan _processTimeout = TimeSpan.FromSeconds(120);
    private bool _disposed;

    /// <summary>
    /// Creates a new WhisperCppTranscriberAdapter for whisper.cpp (C++).
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

        // Validate binary at construction time
        _whisperBinaryPath = ResolveBinaryPath(whisperBinaryPath);
    }

    /// <summary>
    /// Resolves the whisper binary path, validating it exists at construction time.
    /// If bare name (no directory separators), searches PATH. If still not found,
    /// falls back to "whisper" (will fail at transcription time with a clear error).
    /// </summary>
    private static string ResolveBinaryPath(string? binaryPath)
    {
        if (binaryPath == null || !binaryPath.Contains(Path.DirectorySeparatorChar))
        {
            var found = WhisperCppModelManager.FindWhisperBinary();
            if (found != null)
                return found;

            // User-specified binary name (no path) — trust it may be on PATH.
            // A bare name like "whisper" or "whisper-cpp" will be resolved by
            // Process.Start at transcription time.
            if (binaryPath != null)
                return binaryPath;

            // No binary found and no user override — fail early with a clear message.
            throw new TranscriberException(
                "Whisper binary not found. Install whisper.cpp or set WhisperCppBinaryPath in config.\n" +
                "  Install: https://github.com/ggerganov/whisper.cpp");
        }

        // If it's a full path, verify it exists
        if (File.Exists(binaryPath))
            return binaryPath;

        throw new TranscriberException(
            $"Whisper binary not found at specified path: {binaryPath}");
    }

    public async Task<string> TranscribeAsync(byte[] wavBytes, string fileName = "audio.wav", CancellationToken ct = default)
    {
        if (wavBytes == null || wavBytes.Length == 0)
            throw new ArgumentNullException(nameof(wavBytes), "WAV bytes must not be null or empty.");

        // whisper.cpp uses pre-downloaded .bin model files
        var modelPath = _modelManager.GetModelPath(_modelName);
        if (!File.Exists(modelPath))
            throw new TranscriberException(
                $"Whisper model '{_modelName}' not found. Please download it first via /reconfigure → Speech-To-Text.");

        // Link a timeout CTS to the caller's token (120 second process timeout)
        using var timeoutCts = new CancellationTokenSource(_processTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var linkedCt = linkedCts.Token;

        // Write to a unique temp file for whisper CLI to process (random name avoids concurrent collisions)
        var tempDir = Path.Combine(Path.GetTempPath(), "aray-code");
        Directory.CreateDirectory(tempDir);
        var uniqueName = $"{Path.GetRandomFileName()}.wav";
        var tempFile = Path.Combine(tempDir, uniqueName);

        try
        {
            await File.WriteAllBytesAsync(tempFile, wavBytes, linkedCt).ConfigureAwait(false);

            var psi = new ProcessStartInfo
            {
                FileName = _whisperBinaryPath,
                // whisper.cpp CLI: pass .bin model file path as argument
                Arguments = $"--model \"{modelPath}\" --output_dir \"{tempDir}\" --output_format txt \"{tempFile}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)
                ?? throw new TranscriberException($"Failed to start whisper process. Binary: {_whisperBinaryPath}");

            try
            {
                // Read both streams concurrently to avoid deadlock
                var outputTask = process.StandardOutput.ReadToEndAsync(linkedCt);
                var errorTask = process.StandardError.ReadToEndAsync(linkedCt);
                await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync(linkedCt)).ConfigureAwait(false);

                var output = await outputTask.ConfigureAwait(false);
                var error = await errorTask.ConfigureAwait(false);

                if (process.ExitCode != 0)
                {
                    throw new TranscriberException($"whisper exited with code {process.ExitCode}: {error}");
                }

                // Output file will be same name but .txt extension (also unique per temp file)
                var outputFile = Path.ChangeExtension(tempFile, ".txt");
                if (File.Exists(outputFile))
                {
                    var result = await File.ReadAllTextAsync(outputFile, linkedCt).ConfigureAwait(false);
                    return result.Trim();
                }

                // If no output file, return stdout (some whisper versions output to stdout)
                return output.Trim();
            }
            catch
            {
                // On any exception, kill the process
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                throw;
            }
        }
        finally
        {
            // Cleanup temp files — best effort is correct for cleanup
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

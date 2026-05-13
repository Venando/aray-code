using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT.Transcriber;

/// <summary>
/// <c>ITranscriber</c> implementation using <c>uv</c> + <c>faster-whisper</c>.
///
/// <para>
/// Runs faster-whisper transcription as a one-shot subprocess via <c>uv run</c>.
/// No Python installation, package management, or PATH configuration required —
/// <c>uv</c> handles the full Python toolchain automatically.
/// </para>
///
/// <para>
/// This is the replacement for the legacy Python openai-whisper code path
/// previously embedded within the <c>whisper-cpp</c> provider. The old approach
/// is marked <see cref="ObsoleteAttribute"/> and will be removed in a future release.
/// </para>
/// </summary>
public sealed class FasterWhisperTranscriberAdapter : ITranscriber
{
    private readonly FasterWhisperEnvironment _environment;
    private readonly FasterWhisperModelManager _modelManager;
    private readonly string _modelName;
    private readonly TimeSpan _processTimeout = TimeSpan.FromSeconds(120);
    private bool _disposed;

    /// <summary>
    /// Creates a new <c>FasterWhisperTranscriberAdapter</c>.
    /// </summary>
    /// <param name="environment">Environment manager (handles uv + project).</param>
    /// <param name="modelManager">Model manager for cache/directory info.</param>
    /// <param name="modelName">faster-whisper model name (e.g. "base", "small.en").</param>
    public FasterWhisperTranscriberAdapter(
        FasterWhisperEnvironment environment,
        FasterWhisperModelManager modelManager,
        string modelName)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
        _modelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
    }

    /// <inheritdoc />
    public async Task<string> TranscribeAsync(
        byte[] wavBytes,
        string fileName = "audio.wav",
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (wavBytes == null || wavBytes.Length == 0)
            throw new ArgumentNullException(nameof(wavBytes), "WAV bytes must not be null or empty.");

        // Verify model is available (will throw on transcription if not, but early check is friendlier)
        if (!FasterWhisperModelManager.IsModelCached(_modelName))
        {
            throw new TranscriberException(
                $"faster-whisper model '{_modelName}' is not cached. " +
                $"Run the configuration wizard or ensure the model is downloaded first.");
        }

        // Verify uv is available
        if (!FasterWhisperEnvironment.IsUvAvailable())
        {
            throw new TranscriberException(
                $"uv is not installed. Install it with: {FasterWhisperEnvironment.GetInstallInstructions()}");
        }

        // Write WAV to a unique temp file
        var tempDir = Path.Combine(Path.GetTempPath(), "openclaw-ptt");
        Directory.CreateDirectory(tempDir);
        var uniqueName = $"{Path.GetRandomFileName()}.wav";
        var tempFile = Path.Combine(tempDir, uniqueName);

        // Link a timeout CTS to the caller's token
        using var timeoutCts = new CancellationTokenSource(_processTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var linkedCt = linkedCts.Token;

        try
        {
            await File.WriteAllBytesAsync(tempFile, wavBytes, linkedCt).ConfigureAwait(false);

            var pythonCmd = FasterWhisperEnvironment.BuildTranscribeCommand(_modelName, tempFile);
            var psi = _environment.CreateProcessStartInfo(pythonCmd);

            using var process = Process.Start(psi)
                ?? throw new TranscriberException($"Failed to start uv run for faster-whisper transcription.");

            try
            {
                // Read both streams concurrently to avoid deadlock
                var stdoutTask = process.StandardOutput.ReadToEndAsync(linkedCt);
                var stderrTask = process.StandardError.ReadToEndAsync(linkedCt);
                await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(linkedCt))
                    .ConfigureAwait(false);

                var output = await stdoutTask.ConfigureAwait(false);
                var error = await stderrTask.ConfigureAwait(false);

                if (process.ExitCode != 0)
                {
                    throw new TranscriberException(
                        $"faster-whisper exited with code {process.ExitCode}: {error}");
                }

                return output.Trim();
            }
            catch
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                throw;
            }
        }
        finally
        {
            // Cleanup temp WAV — best effort
            try { if (File.Exists(tempFile)) File.Delete(tempFile); }
            catch { /* best effort */ }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
            _disposed = true;
    }
}

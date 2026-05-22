namespace OpenClawPTT.Services;

public interface IAudioService : IDisposable
{
    /// <summary>
    /// Optional callback invoked during transcription lifecycle.
    /// Phase is the current stage; errorMessage is non-null only for Failed/TimedOut.
    /// </summary>
    Action<TranscriptionPhase, string?>? TranscriptionStatusCallback { get; set; }

    bool IsRecording { get; }
    void StartRecording();
    Task<string?> StopAndTranscribeAsync(CancellationToken ct = default);

    /// <summary>Stops recording and discards the audio without transcribing.</summary>
    void StopDiscard();

    /// <summary>
    /// Shows the transcribing state in the recording bottom panel (spinner).
    /// Called after recording stops and before transcription completes.
    /// </summary>
    void ShowTranscribing();

    /// <summary>
    /// Shows the confirmation state in the recording bottom panel.
    /// Displays the transcribed text with send/discard instructions.
    /// </summary>
    void ShowConfirming(string transcribed, double sizeKb);

    /// <summary>
    /// Shows the discarded state in the recording bottom panel for 1.5s, then clears.
    /// </summary>
    void ShowDiscarded();

    /// <summary>
    /// Dismisses the recording bottom panel and restores the default panel.
    /// </summary>
    void DismissRecordingPanel();

    /// <summary>
    /// Re-creates the transcriber after a config change (e.g. STT provider/model switched).
    /// </summary>
    void RecreateTranscriber(AppConfig config, IColorConsole console);

    /// <summary>
    /// Re-creates the audio recorder after a config change (e.g. SampleRate, Channels).
    /// Skips if a recording is in progress — the new configuration applies on the next
    /// recording cycle.
    /// </summary>
    void RecreateRecorder(AppConfig config, IColorConsole console);

    /// <summary>
    /// Verifies the transcriber is functional by sending a short silence WAV
    /// through the pipeline. Throws if the transcriber fails.
    /// </summary>
    Task VerifyTranscriberAsync(AppConfig config, IColorConsole console, CancellationToken ct = default);
}
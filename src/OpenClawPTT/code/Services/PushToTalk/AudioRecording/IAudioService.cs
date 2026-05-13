namespace OpenClawPTT.Services;

public interface IAudioService : IDisposable
{
    bool IsRecording { get; }
    void StartRecording();
    Task<string?> StopAndTranscribeAsync(CancellationToken ct = default);

    /// <summary>Stops recording and discards the audio without transcribing.</summary>
    void StopDiscard();

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
}
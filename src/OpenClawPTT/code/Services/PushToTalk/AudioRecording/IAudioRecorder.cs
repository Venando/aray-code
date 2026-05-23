namespace OpenClawPTT;

/// <summary>
/// Abstracts the audio recording backend so AudioService can be tested
/// without requiring a real microphone or sox/NAudio.
/// </summary>
public interface IAudioRecorder : IDisposable
{
    bool IsRecording { get; }
    void StartRecording();
    byte[] StopRecording();

    /// <summary>
    /// Returns the current RMS audio level normalized to 0.0–1.0.
    /// Returns -1 if audio level monitoring is unavailable for this platform/backend.
    /// Only valid while recording; behavior is undefined after StopRecording.
    /// </summary>
    float GetCurrentAudioLevel();
}

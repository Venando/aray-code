namespace ArayCode.Transcriber;

/// <summary>
/// Interface for speech-to-text providers.
/// Implement this to add support for new STT providers or local models.
/// </summary>
public interface ITranscriber : IDisposable
{
    /// <summary>
    /// Transcribes audio bytes to text.
    /// </summary>
    /// <param name="wavBytes">Raw WAV audio bytes.</param>
    /// <param name="fileName">Optional file name for provider-specific processing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Transcribed text, or string.Empty if no speech detected.</returns>
    Task<string> TranscribeAsync(byte[] wavBytes, string fileName = "audio.wav", CancellationToken ct = default);
}

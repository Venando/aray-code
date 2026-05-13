using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT.Transcriber;

/// <summary>
/// Adapter that wraps the legacy GroqTranscriber to implement the ITranscriber interface.
/// </summary>
public sealed class GroqTranscriberAdapter : ITranscriber
{
    private readonly GroqTranscriber _inner;

    public GroqTranscriberAdapter(string apiKey, string model = "whisper-large-v3-turbo", int retryCount = 0, int retryDelayMs = 1000, double retryBackoffFactor = 2.0)
    {
        _inner = new GroqTranscriber(apiKey, model, retryCount, retryDelayMs, retryBackoffFactor);
    }

    public async Task<string> TranscribeAsync(byte[] wavBytes, string fileName = "audio.wav", CancellationToken ct = default)
    {
        return await _inner.TranscribeAsync(wavBytes, fileName, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _inner.Dispose();
    }
}

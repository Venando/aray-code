namespace ArayCode.TTS;

/// <summary>
/// TTS provider interface
/// </summary>
public interface ITextToSpeech
{
    /// <summary>
    /// Provider name
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Available voices for this provider
    /// </summary>
    IReadOnlyList<string> AvailableVoices { get; }

    /// <summary>
    /// Available models for this provider (for local models)
    /// </summary>
    IReadOnlyList<string> AvailableModels { get; }

    /// <summary>
    /// Synthesize text to audio bytes
    /// </summary>
    Task<byte[]> SynthesizeAsync(string text, string? voice = null, string? model = null, CancellationToken ct = default);
}

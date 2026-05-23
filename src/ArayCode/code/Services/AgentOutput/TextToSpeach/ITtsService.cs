using System;
using System.Threading;
using System.Threading.Tasks;

namespace ArayCode.TTS;

/// <summary>
/// Abstracts the TtsService for testability.
/// Manages TTS provider initialization and ownership transfer.
/// </summary>
public interface ITtsService : IDisposable
{
    /// <summary>Cancellation token for the TTS service lifecycle.</summary>
    CancellationToken CancellationToken { get; }

    /// <summary>The configured TTS provider type.</summary>
    TtsProviderType ProviderType { get; }

    /// <summary>The initialized TTS provider, if any.</summary>
    ITextToSpeech? Provider { get; }

    /// <summary>True if a provider is available and configured.</summary>
    bool IsConfigured { get; }

    /// <summary>Synthesizes text to audio bytes.</summary>
    Task<byte[]> SynthesizeAsync(string text, CancellationToken ct = default);

    /// <summary>Synthesizes text to audio bytes with a specific voice.</summary>
    Task<byte[]> SynthesizeAsync(string text, string voice, CancellationToken ct = default);

    /// <summary>Synthesizes text to audio bytes with a specific voice and model.</summary>
    Task<byte[]> SynthesizeAsync(string text, string voice, string model, CancellationToken ct = default);

    /// <summary>
    /// Releases ownership of the TTS provider, transferring it to the caller.
    /// After calling this, Dispose() will not dispose the provider.
    /// </summary>
    ITextToSpeech? ReleaseProvider();
}

using System.Text.Json.Serialization;
using OpenClawPTT.Services;

namespace OpenClawPTT.TTS;

/// <summary>
/// TTS provider types
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TtsProviderType
{
    OpenAI,
    Edge,
    // Coqui + Python removed — use CoquiUv
    Piper,
    ElevenLabs,
    /// <summary>
    /// Coqui TTS via <c>uv</c> — automatic Python/packages/dependencies.
    /// Replacement for <see cref="Coqui"/> and <see cref="Python"/>.
    /// </summary>
    CoquiUv,
}

/// <summary>
/// TTS service - manages TTS providers and configuration
/// </summary>
public sealed class TtsService : ITtsService
{
    private ITextToSpeech? _provider;
    private readonly TtsProviderType _providerType;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public CancellationToken CancellationToken => _cts.Token;
    
    public TtsProviderType ProviderType => _providerType;
    public ITextToSpeech? Provider => _provider;
    public bool IsConfigured => _provider != null;

    public TtsService(AppConfig config, IColorConsole console)
    {
        _providerType = config.TtsProvider;

        _provider = _providerType switch
        {
            TtsProviderType.OpenAI => new Providers.OpenAiTtsProvider(config.TtsOpenAiApiKey ?? config.OpenAiApiKey ?? throw new InvalidOperationException("OpenAI API key not configured")),
            TtsProviderType.CoquiUv => new Providers.CoquiUvTtsProvider(
                console,
                config.CustomDataDir ?? config.DataDir,
                config.CoquiModelName ?? "tts_models/multilingual/mxtts/vits",
                config.CoquiModelPath,
                config.CoquiConfigPath,
                config.EspeakNgPath,
                true),
            TtsProviderType.Piper => new Providers.PiperTtsProvider(config.PiperPath ?? "piper", config.PiperModelPath ?? "", config.PiperVoice ?? "en_US-lessac"),
            TtsProviderType.Edge => config.TtsSubscriptionKey != null
                ? new Providers.EdgeTtsProvider(config.TtsSubscriptionKey, config.TtsRegion ?? "eastus")
                : null,
            _ => null
        };

        if (_provider == null && _providerType == TtsProviderType.Edge)
        {
            // Edge with null key — warn but don't crash (TtsService still works, TTS just silent)
            console.PrintWarning(
                $"TTS provider '{_providerType}' requires a subscription key. " +
                "Set 'TtsSubscriptionKey' and 'TtsRegion' in configuration.");
        }
        else if (_provider == null)
        {
            throw new InvalidOperationException($"Failed to initialize TTS provider: {_providerType}");
        }

        if (_provider is Providers.CoquiUvTtsProvider coquiUv)
        {
            try
            {
                coquiUv.InitializeAsync(_cts.Token).GetAwaiter().GetResult();
            }
            catch (AggregateException ae)
            {
                // Unwrap AggregateException from GetAwaiter().GetResult()
                throw ae.InnerException ?? ae;
            }
        }

    }

    /// <summary>
    /// Synthesize text to audio
    /// </summary>
    public async Task<byte[]> SynthesizeAsync(string text, CancellationToken ct = default)
    {
        if (_provider == null)
        {
            throw new InvalidOperationException("TTS provider not configured");
        }

        return await _provider.SynthesizeAsync(
            text, 
            voice: null, 
            model: null, 
            ct);
    }

    /// <summary>
    /// Synthesize text with specific voice
    /// </summary>
    public async Task<byte[]> SynthesizeAsync(string text, string voice, CancellationToken ct = default)
    {
        if (_provider == null)
        {
            throw new InvalidOperationException("TTS provider not configured");
        }

        return await _provider.SynthesizeAsync(text, voice, null, ct);
    }

    /// <summary>
    /// Synthesize text with specific voice and model
    /// </summary>
    public async Task<byte[]> SynthesizeAsync(string text, string voice, string model, CancellationToken ct = default)
    {
        if (_provider == null)
        {
            throw new InvalidOperationException("TTS provider not configured");
        }

        return await _provider.SynthesizeAsync(text, voice, model, ct);
    }

    /// <summary>
    /// Releases ownership of the TTS provider, transferring it to the caller.
    /// After calling this, Dispose() will not dispose the provider.
    /// </summary>
    public ITextToSpeech? ReleaseProvider()
    {
        var provider = _provider;
        _provider = null;  // Prevent Dispose from killing the provider
        return provider;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cts.Cancel();
            _cts.Dispose();
            if (_provider is IAsyncDisposable asyncDisposable)
                asyncDisposable.DisposeAsync().Preserve();
            else
                (_provider as IDisposable)?.Dispose();
            _disposed = true;
        }
    }
}

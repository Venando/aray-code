using System.Text.Json.Serialization;
using OpenClawPTT.Services;

namespace OpenClawPTT.TTS;

/// <summary>
/// TTS provider types.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TtsProviderType
{
    OpenAI,
    Edge,
    // Coqui + Python removed — use CoquiUv
    Piper,
    /// <summary>
    /// Coqui TTS via <c>uv</c> — automatic Python/packages/dependencies.
    /// Replacement for <see cref="TtsProviderType.Coqui"/> and <see cref="TtsProviderType.Python"/>.
    /// </summary>
    CoquiUv,
}

/// <summary>
/// TTS service — manages TTS provider lifecycle and exposes unified
/// synthesis methods. All <c>SynthesizeAsync</c> overloads delegate to
/// a single private implementation (DRY).
/// </summary>
public sealed class TtsService : ITtsService
{
    private ITextToSpeech? _provider;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public CancellationToken CancellationToken => _cts.Token;
    public TtsProviderType ProviderType { get; }
    public ITextToSpeech? Provider => _provider;
    public bool IsConfigured => _provider != null;

    public TtsService(AppConfig config, IColorConsole console)
    {
        ProviderType = config.TtsProvider;

        _provider = CreateProvider(config, console);

        if (_provider == null && ProviderType == TtsProviderType.Edge)
        {
            // Edge with null key — warn but don't crash (TTS just silent)
            console.PrintWarning(
                $"TTS provider '{ProviderType}' requires a subscription key. " +
                "Set 'TtsSubscriptionKey' and 'TtsRegion' in configuration.");
        }
        else if (_provider == null)
        {
            throw new InvalidOperationException($"Failed to initialize TTS provider: {ProviderType}");
        }

        // CoquiUv requires async initialization — block in constructor for simplicity
        if (_provider is Providers.CoquiUvTtsProvider coquiUv)
        {
            try
            {
                coquiUv.InitializeAsync(_cts.Token).GetAwaiter().GetResult();
            }
            catch (AggregateException ae)
            {
                throw ae.InnerException ?? ae;
            }
        }
    }

    /// <summary>Synthesizes text to audio bytes.</summary>
    public Task<byte[]> SynthesizeAsync(string text, CancellationToken ct = default)
        => SynthesizeCoreAsync(text, voice: null, model: null, ct);

    /// <summary>Synthesizes text to audio bytes with a specific voice.</summary>
    public Task<byte[]> SynthesizeAsync(string text, string voice, CancellationToken ct = default)
        => SynthesizeCoreAsync(text, voice, model: null, ct);

    /// <summary>Synthesizes text to audio bytes with a specific voice and model.</summary>
    public Task<byte[]> SynthesizeAsync(string text, string voice, string model, CancellationToken ct = default)
        => SynthesizeCoreAsync(text, voice, model, ct);

    /// <summary>Core synthesis implementation — all overloads delegate here (DRY).</summary>
    private async Task<byte[]> SynthesizeCoreAsync(string text, string? voice, string? model, CancellationToken ct)
    {
        if (_provider == null)
            throw new InvalidOperationException("TTS provider not configured");

        return await _provider.SynthesizeAsync(text, voice, model, ct);
    }

    /// <summary>
    /// Releases ownership of the TTS provider, transferring it to the caller.
    /// After calling this, Dispose() will not dispose the provider.
    /// </summary>
    public ITextToSpeech? ReleaseProvider()
    {
        var provider = _provider;
        _provider = null;
        return provider;
    }

    /// <summary>Creates the appropriate TTS provider from configuration.</summary>
    private static ITextToSpeech? CreateProvider(AppConfig config, IColorConsole console)
    {
        return config.TtsProvider switch
        {
            TtsProviderType.OpenAI => new Providers.OpenAiTtsProvider(
                config.TtsOpenAiApiKey ?? config.OpenAiApiKey
                    ?? throw new InvalidOperationException("OpenAI API key not configured")),

            TtsProviderType.CoquiUv => new Providers.CoquiUvTtsProvider(
                console,
                config.CustomDataDir ?? config.DataDir,
                config.CoquiModelName ?? "tts_models/multilingual/mxtts/vits",
                config.CoquiModelPath,
                config.CoquiConfigPath,
                config.EspeakNgPath,
                debugLog: true),

            TtsProviderType.Piper => new Providers.PiperTtsProvider(
                config.PiperPath ?? "piper",
                config.PiperModelPath ?? "",
                config.PiperVoice ?? "en_US-lessac"),

            TtsProviderType.Edge => config.TtsSubscriptionKey != null
                ? new Providers.EdgeTtsProvider(config.TtsSubscriptionKey, config.TtsRegion ?? "eastus")
                : null,

            _ => null,
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _cts.Dispose();

        if (_provider is IAsyncDisposable asyncDisposable)
            asyncDisposable.DisposeAsync().Preserve();
        else
            (_provider as IDisposable)?.Dispose();
    }
}

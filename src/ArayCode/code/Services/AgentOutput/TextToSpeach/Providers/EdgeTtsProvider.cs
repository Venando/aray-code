using System.Net.Http;
using System.Text;

namespace ArayCode.TTS.Providers;

/// <summary>
/// Microsoft Azure Cognitive Services TTS provider.
/// Uses the Azure Speech REST API directly.
/// </summary>
public sealed class EdgeTtsProvider : ITextToSpeech, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _subscriptionKey;
    private readonly string _region;

    private bool _disposed;

    public string ProviderName => "Azure TTS";

    // https://learn.microsoft.com/en-us/azure/ai-services/speech-service/rest-text-to-speech
    public IReadOnlyList<string> AvailableVoices { get; } =
    [
        "en-US-AriaNeural", "en-US-GuyNeural", "en-US-JennyNeural", "en-US-SaraNeural",
        "en-US-EmmaNeural", "en-US-RogerNeural", "en-US-AshleyNeural", "en-US-CoreyNeural",
        "en-GB-SoniaNeural", "en-GB-RyanNeural",
        "de-DE-KatjaNeural", "de-DE-ConradNeural",
        "fr-FR-DeniseNeural", "fr-FR-HenriNeural",
        "es-ES-ElviraNeural", "es-MX-DaliaNeural",
    ];

    public IReadOnlyList<string> AvailableModels { get; } = [];

    public EdgeTtsProvider(string? subscriptionKey = null, string region = "eastus")
    {
        if (string.IsNullOrEmpty(subscriptionKey))
        {
            throw new InvalidOperationException(
                "Azure TTS requires a subscription key. " +
                "Set the 'TtsSubscriptionKey' configuration option.");
        }

        _subscriptionKey = subscriptionKey;
        _region = region;
        _http = new HttpClient();
    }

    public async Task<byte[]> SynthesizeAsync(string text, string? voice = null, string? model = null, CancellationToken ct = default)
    {
        var selectedVoice = ResolveVoice(voice);
        var ssml = SsmlHelper.BuildSsml(text, selectedVoice);

        var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl());
        request.Headers.Add("Ocp-Apim-Subscription-Key", _subscriptionKey);
        request.Content = new StringContent(ssml, Encoding.UTF8, "application/ssml+xml");
        request.Headers.Add("X-Microsoft-OutputFormat", "audio-16khz-16bit-mono-wav");

        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>Validates and resolves the voice name, falling back to default.</summary>
    private string ResolveVoice(string? voice)
    {
        var selected = voice ?? "en-US-AriaNeural";

        if (!AvailableVoices.Contains(selected, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Invalid voice '{selected}'. Available: {string.Join(", ", AvailableVoices)}");
        }

        return selected;
    }

    /// <summary>Builds the Azure TTS REST API URL for the configured region.</summary>
    private string BuildUrl() =>
        $"https://{_region}.tts.speech.microsoft.com/cognitiveservices/v1";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}

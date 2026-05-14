using System.Net.Http.Json;

namespace OpenClawPTT.TTS.Providers;

/// <summary>
/// OpenAI TTS provider (tts-1, tts-1-hd).
/// </summary>
public sealed class OpenAiTtsProvider : ITextToSpeech, IDisposable
{
    private readonly HttpClient _http;
    private bool _disposed;

    public string ProviderName => "OpenAI";

    // https://platform.openai.com/docs/guides/text-to-speech
    public IReadOnlyList<string> AvailableVoices { get; } =
    [
        "alloy", "echo", "fable", "onyx", "nova", "shimmer",
    ];

    public IReadOnlyList<string> AvailableModels { get; } =
    [
        "tts-1", "tts-1-hd",
    ];

    public OpenAiTtsProvider(string apiKey)
    {
        ArgumentNullException.ThrowIfNull(apiKey);

        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    public async Task<byte[]> SynthesizeAsync(string text, string? voice = null, string? model = null, CancellationToken ct = default)
    {
        var selectedVoice = ResolveVoice(voice);
        var selectedModel = ResolveModel(model);

        var request = new
        {
            model = selectedModel,
            voice = selectedVoice,
            input = text,
            response_format = "wav",
        };

        var response = await _http.PostAsJsonAsync(
            "https://api.openai.com/v1/audio/speech",
            request,
            ct);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>Validates the voice, falling back to "alloy".</summary>
    private string ResolveVoice(string? voice)
    {
        var selected = voice ?? "alloy";
        if (!AvailableVoices.Contains(selected, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Invalid voice '{selected}'. Available: {string.Join(", ", AvailableVoices)}");
        }
        return selected;
    }

    /// <summary>Validates the model, falling back to "tts-1".</summary>
    private string ResolveModel(string? model)
    {
        var selected = model ?? "tts-1";
        if (!AvailableModels.Contains(selected, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Invalid model '{selected}'. Available: {string.Join(", ", AvailableModels)}");
        }
        return selected;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}

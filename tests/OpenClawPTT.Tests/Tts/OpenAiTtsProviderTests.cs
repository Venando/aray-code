using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.TTS;
using OpenClawPTT.TTS.Providers;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Tests for OpenAiTtsProvider error paths and validation.
/// </summary>
public class OpenAiTtsProviderTests
{
    #region Constructor Validation

    [Fact]
    public void Constructor_WithNullApiKey_ThrowsArgumentNullException()
    {
        // The contract is ArgumentNullException per standard .NET convention.
        var ex = Record.Exception(() => new OpenAiTtsProvider(null!));
        Assert.NotNull(ex);
        Assert.IsAssignableFrom<ArgumentNullException>(ex);
    }

    [Fact]
    public void Constructor_WithEmptyApiKey_IsAccepted()
    {
        // OpenAiTtsProvider's null guard is only for null (using null!), not empty string.
        // Empty string is accepted at construction; the API call will fail later.
        var ex = Record.Exception(() => new OpenAiTtsProvider(""));
        Assert.Null(ex);
    }

    #endregion

    #region Voice Validation

    [Fact]
    public async Task SynthesizeAsync_WithInvalidVoice_ThrowsArgumentException()
    {
        // Voice validation happens at SynthesizeAsync time.
        var provider = new OpenAiTtsProvider("sk-test-key-for-voice-check");

        // Act & Assert: an invalid voice name should be rejected before any HTTP call.
        var ex = await Record.ExceptionAsync(() =>
            provider.SynthesizeAsync("Hello world", voice: "not-a-real-voice"));

        Assert.NotNull(ex);
        Assert.IsAssignableFrom<ArgumentException>(ex);
    }

    #endregion

    #region Model Validation

    [Fact]
    public async Task SynthesizeAsync_WithInvalidModel_ThrowsArgumentException()
    {
        var provider = new OpenAiTtsProvider("sk-test-key-for-model-check");

        // Act & Assert: an invalid model name should be rejected before any HTTP call.
        var ex = await Record.ExceptionAsync(() =>
            provider.SynthesizeAsync("Hello world", model: "not-a-real-model"));

        Assert.NotNull(ex);
        Assert.IsAssignableFrom<ArgumentException>(ex);
    }

    #endregion

    #region HTTP Error Propagation

    [Fact]
    public async Task SynthesizeAsync_Http503_ExceptionPropagates()
    {
        // Arrange: inject a stub handler that returns 503 Service Unavailable.
        var handler = new HttpMessageHandlerStub(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                ReasonPhrase = "Service Unavailable"
            };
            return Task.FromResult(response);
        });

        using var http = new HttpClient(handler);
        var provider = new OpenAiTtsProviderViaReflection("sk-test-key", http);

        // Act & Assert: HTTP errors should propagate from EnsureSuccessStatusCode.
        var ex = await Record.ExceptionAsync(() =>
            provider.SynthesizeAsync("Hello world", voice: "alloy", model: "tts-1"));

        Assert.NotNull(ex);
        Assert.IsAssignableFrom<HttpRequestException>(ex);
    }

    #endregion

    #region Null Audio Text

    [Fact]
    public async Task SynthesizeAsync_WithNullAudioText_HandledGracefully()
    {
        // Arrange: a handler that succeeds on any request to confirm
        // null text is passed through without crashing.
        var handler = new HttpMessageHandlerStub(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[44]) // minimal WAV bytes
            };
            return Task.FromResult(response);
        });

        using var http = new HttpClient(handler);
        var provider = new OpenAiTtsProviderViaReflection("sk-test-key", http);

        // Act: null text should be passed to the API (the API may reject it,
        // but it should not crash locally).
        var audio = await provider.SynthesizeAsync(null!, voice: "alloy", model: "tts-1");
        Assert.NotNull(audio);
    }

    #endregion

    #region Test Doubles

    /// <summary>
    /// Allows injecting a test HttpClient into the real provider while
    /// preserving all actual synthesis logic.
    /// </summary>
    private sealed class OpenAiTtsProviderViaReflection : ITextToSpeech
    {
        private readonly OpenAiTtsProvider _inner;

        public string ProviderName => _inner.ProviderName;
        public IReadOnlyList<string> AvailableVoices => _inner.AvailableVoices;
        public IReadOnlyList<string> AvailableModels => _inner.AvailableModels;

        public OpenAiTtsProviderViaReflection(string apiKey, HttpClient http)
        {
            _inner = new OpenAiTtsProvider(apiKey);
            var field = typeof(OpenAiTtsProvider).GetField("_http",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field!.SetValue(_inner, http);
        }

        public async Task<byte[]> SynthesizeAsync(string text, string? voice = null,
            string? model = null, CancellationToken ct = default)
            => await _inner.SynthesizeAsync(text, voice, model, ct);
    }

    #endregion
}

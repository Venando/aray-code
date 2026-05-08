using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.TTS;
using OpenClawPTT.TTS.Providers;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Tests for EdgeTtsProvider error paths and validation.
/// </summary>
public class EdgeTtsProviderTests
{
    #region Constructor Validation

    [Fact]
    public void Constructor_WithNullSubscriptionKey_ThrowsInvalidOperationException()
    {
        // The provider requires a subscription key to be configured.
        // Null key should fail fast at construction time.
        var ex = Record.Exception(() => new EdgeTtsProvider(null));
        Assert.NotNull(ex);
        Assert.IsAssignableFrom<InvalidOperationException>(ex);
    }

    [Fact]
    public void Constructor_WithEmptySubscriptionKey_ThrowsInvalidOperationException()
    {
        var ex = Record.Exception(() => new EdgeTtsProvider(""));
        Assert.NotNull(ex);
        Assert.IsAssignableFrom<InvalidOperationException>(ex);
    }

    [Fact]
    public void Constructor_WithWhitespaceSubscriptionKey_IsAccepted()
    {
        // EdgeTtsProvider only validates null/empty via string.IsNullOrEmpty.
        // Pure whitespace "   " is not null/empty, so it is accepted at construction.
        // The API call will fail later when used, which is appropriate.
        var ex = Record.Exception(() => new EdgeTtsProvider("   "));
        Assert.Null(ex);
    }

    #endregion

    #region Voice Validation

    [Fact]
    public async Task SynthesizeAsync_WithInvalidVoiceName_ThrowsArgumentException()
    {
        // The provider is voice-agnostic at construction — the voice check
        // happens inside SynthesizeAsync.
        var provider = new EdgeTtsProvider("fake-key-for-voice-validation");

        // Act & Assert: invalid voice name should be rejected before any HTTP call.
        var ex = await Record.ExceptionAsync(() =>
            provider.SynthesizeAsync("Hello world", voice: "not-a-real-voice"));

        Assert.NotNull(ex);
        Assert.IsAssignableFrom<ArgumentException>(ex);
    }

    #endregion

    #region HTTP Error Propagation

    [Fact]
    public async Task SynthesizeAsync_HttpError_ExceptionPropagates()
    {
        // Arrange: inject a handler that returns an HTTP 503.
        var handler = new HttpMessageHandlerStub(s =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                ReasonPhrase = "Service Unavailable"
            };
            return Task.FromResult(response);
        });

        using var http = new HttpClient(handler);
        var provider = new EdgeTtsProviderViaReflection("fake-key", http);

        // Act & Assert: HTTP errors should propagate.
        var ex = await Record.ExceptionAsync(() =>
            provider.SynthesizeAsync("Hello world", voice: "en-US-AriaNeural"));

        Assert.NotNull(ex);
        // EnsureSuccessStatusCode throws HttpRequestException on non-2xx
        Assert.IsAssignableFrom<HttpRequestException>(ex);
    }

    #endregion

    #region Null Audio Text

    [Fact]
    public async Task SynthesizeAsync_WithNullAudioText_HandledGracefully()
    {
        // Arrange: a handler that succeeds on valid SSML to confirm no early crash.
        var handler = new HttpMessageHandlerStub(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[44]) // minimal WAV header
            };
            return Task.FromResult(response);
        });

        using var http = new HttpClient(handler);
        var provider = new EdgeTtsProviderViaReflection("fake-key", http);

        // Act & Assert: null text is escaped to empty — should not throw.
        var audio = await provider.SynthesizeAsync(null!, voice: "en-US-AriaNeural");
        Assert.NotNull(audio);
    }

    #endregion

    #region Test Doubles

    /// <summary>
    /// Reflects into the real provider to inject a test HttpClient while
    /// preserving all actual logic.
    /// </summary>
    private sealed class EdgeTtsProviderViaReflection : ITextToSpeech
    {
        private readonly EdgeTtsProvider _inner;

        public string ProviderName => _inner.ProviderName;
        public IReadOnlyList<string> AvailableVoices => _inner.AvailableVoices;
        public IReadOnlyList<string> AvailableModels => _inner.AvailableModels;

        public EdgeTtsProviderViaReflection(string subscriptionKey, HttpClient http)
        {
            _inner = new EdgeTtsProvider(subscriptionKey);
            var field = typeof(EdgeTtsProvider).GetField("_http",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field!.SetValue(_inner, http);
        }

        public async Task<byte[]> SynthesizeAsync(string text, string? voice = null,
            string? model = null, CancellationToken ct = default)
            => await _inner.SynthesizeAsync(text, voice, model, ct);
    }

    #endregion
}

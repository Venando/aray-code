using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ArayCode.Tests;

/// <summary>
/// Stability and edge-case tests for <see cref="GroqTranscriber"/>.
/// </summary>
public class GroqTranscriberTests : IDisposable
{
    private const string ValidApiKey = "gsk_test_valid_key_12345";
    private static readonly byte[] SampleWav = new byte[] { 0x52, 0x49, 0x46, 0x46 }; // "RIFF"

    // -------------------------------------------------------------------------
    // Test doubles
    // -------------------------------------------------------------------------

    /// <summary>
    /// A fake <see cref="HttpMessageHandler"/> that records invocations and
    /// returns configurable responses.
    /// </summary>
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        public int SendAsync_CallCountAtDispose { get; private set; }

        public HttpResponseMessage? ResponseToReturn { get; set; }
        public Exception? ExceptionToThrow { get; set; }
        public int Times_CanRetry { get; set; } = int.MaxValue;
        public int Times_Called { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Times_Called++;
            SendAsync_Count++;

            if (ExceptionToThrow != null)
                throw ExceptionToThrow;

            if (ResponseToReturn == null)
                throw new InvalidOperationException("ResponseToReturn must be set.");

            return await Task.FromResult(ResponseToReturn);
        }

        public int SendAsync_Count { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                SendAsync_CallCountAtDispose = Times_Called;
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Creates a <see cref="GroqTranscriber"/> with a fake <see cref="HttpClient"/>
    /// backed by <paramref name="handler"/>.
    /// </summary>
    private static GroqTranscriber CreateWithFakeHttp(FakeHttpMessageHandler handler, int retryCount = 3, int retryDelayMs = 10, double backoffFactor = 2.0)
    {
        var transcriber = new GroqTranscriber(ValidApiKey, retryCount: retryCount, retryDelayMs: retryDelayMs, retryBackoffFactor: backoffFactor);

        var httpField = typeof(GroqTranscriber)
            .GetField("_http", BindingFlags.NonPublic | BindingFlags.Instance)!;

        httpField.SetValue(transcriber, new HttpClient(handler));

        return transcriber;
    }

    // -------------------------------------------------------------------------
    // Constructor validation
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ThrowsArgumentNullException_WhenApiKeyNullOrEmpty(string? apiKey)
    {
        Assert.Throws<ArgumentNullException>(() => new GroqTranscriber(apiKey!));
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenApiKeyIsNull_ThrowsWithParamName()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new GroqTranscriber(null!));
        Assert.Equal("apiKey", ex.ParamName);
    }

    // -------------------------------------------------------------------------
    // TranscribeAsync parameter validation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TranscribeAsync_ThrowsArgumentNullException_WhenWavBytesIsNull()
    {
        using var transcriber = new GroqTranscriber(ValidApiKey);
        await Assert.ThrowsAsync<ArgumentNullException>("wavBytes",
            () => transcriber.TranscribeAsync(null!));
    }


    [Fact]
    public async Task TranscribeAsync_ThrowsArgumentNullException_WhenWavBytesIsEmpty()
    {
        using var transcriber = new GroqTranscriber(ValidApiKey);
        await Assert.ThrowsAsync<ArgumentNullException>("wavBytes",
            () => transcriber.TranscribeAsync(Array.Empty<byte>()));
    }

    // -------------------------------------------------------------------------
    // ObjectDisposedException
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TranscribeAsync_ThrowsObjectDisposedException_AfterDispose()
    {
        var transcriber = new GroqTranscriber(ValidApiKey);
        transcriber.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => transcriber.TranscribeAsync(SampleWav));
    }

    // -------------------------------------------------------------------------
    // Dispose called twice
    // -------------------------------------------------------------------------

    [Fact]
    public void Dispose_CalledTwice_DoesNotCrash()
    {
        var transcriber = new GroqTranscriber(ValidApiKey);
        transcriber.Dispose();
        transcriber.Dispose(); // should not throw
    }

    // -------------------------------------------------------------------------
    // HTTP 503 — retries then fails
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TranscribeAsync_Http503_RetriesThenFails_WhenRetryCountIs3()
    {
        var handler = new FakeHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("Service Unavailable")
            }
        };

        using var transcriber = CreateWithFakeHttp(handler, retryCount: 3);

        await Assert.ThrowsAsync<GroqTranscriberException>(
            () => transcriber.TranscribeAsync(SampleWav));

        // 1 initial + 3 retries = 4 total calls
        Assert.Equal(4, handler.Times_Called);
    }

    // -------------------------------------------------------------------------
    // HTTP 429 — retries with backoff
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TranscribeAsync_Http429_RetriesWithBackoff_WhenRetryCountIs2()
    {
        var handler = new FakeHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage((HttpStatusCode)429)
            {
                Content = new StringContent("Rate Limited")
            }
        };

        using var transcriber = CreateWithFakeHttp(handler, retryCount: 2);

        await Assert.ThrowsAsync<GroqTranscriberException>(
            () => transcriber.TranscribeAsync(SampleWav));

        // 1 initial + 2 retries = 3 total attempts
        Assert.Equal(3, handler.Times_Called);
    }

    // -------------------------------------------------------------------------
    // HTTP 401 — fails immediately, no retry
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TranscribeAsync_Http401_FailsImmediately_NoRetry()
    {
        var handler = new FakeHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("Unauthorized")
            }
        };

        using var transcriber = CreateWithFakeHttp(handler, retryCount: 3);

        await Assert.ThrowsAsync<GroqTranscriberException>(
            () => transcriber.TranscribeAsync(SampleWav));

        // Exactly 1 call — no retries for 401
        Assert.Equal(1, handler.Times_Called);
    }

    // -------------------------------------------------------------------------
    // Network exception — retried as transient
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TranscribeAsync_NetworkException_Retries_WithRetryCount3()
    {
        var handler = new FakeHttpMessageHandler
        {
            ExceptionToThrow = new HttpRequestException("Network unreachable")
        };

        using var transcriber = CreateWithFakeHttp(handler, retryCount: 3);

        await Assert.ThrowsAsync<GroqTranscriberException>(
            () => transcriber.TranscribeAsync(SampleWav));

        // 1 initial + 3 retries = 4 total attempts
        Assert.Equal(4, handler.Times_Called);
    }

    // -------------------------------------------------------------------------
    // ExtractText — malformed JSON falls back to raw body
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExtractText_MalformedJson_FallsBackToRawBody()
    {
        var handler = new FakeHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("this is not json {{{")
            }
        };

        using var transcriber = CreateWithFakeHttp(handler, retryCount: 0);

        var result = await transcriber.TranscribeAsync(SampleWav);

        Assert.Equal("this is not json {{{", result);
    }

    // -------------------------------------------------------------------------
    // ExtractText — valid JSON but missing "text" field
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExtractText_ValidJsonMissingTextField_ReturnsRawBody()
    {
        var handler = new FakeHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"model\":\"whisper-large-v3-turbo\"}")
            }
        };

        using var transcriber = CreateWithFakeHttp(handler, retryCount: 0);

        var result = await transcriber.TranscribeAsync(SampleWav);

        Assert.Equal("{\"model\":\"whisper-large-v3-turbo\"}", result);
    }

    // -------------------------------------------------------------------------
    // IsTransientStatusCode — true for 503, 429; false for 400, 401, 404
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(502, true)]
    [InlineData(503, true)]
    [InlineData(504, true)]
    [InlineData(599, true)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(404, false)]
    [InlineData(418, false)] // teapot
    public void IsTransientStatusCode_ReturnsCorrectValue(int statusCode, bool expectedTransient)
    {
        var method = typeof(GroqTranscriber)
            .GetMethod("IsTransientStatusCode", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = method.Invoke(null, new object[] { (HttpStatusCode)statusCode });

        Assert.Equal(expectedTransient, result);
    }

    // -------------------------------------------------------------------------
    // CancellationToken — cancels immediately
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TranscribeAsync_PreCancelledToken_ThrowsOperationCanceledException()
    {
        var handler = new FakeHttpMessageHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("hello")
            }
        };

        using var transcriber = CreateWithFakeHttp(handler, retryCount: 0);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => transcriber.TranscribeAsync(SampleWav, ct: cts.Token));
    }

    // -------------------------------------------------------------------------
    // Default retry count
    // -------------------------------------------------------------------------

    [Fact]
    public void AppConfig_GroqRetryCount_DefaultsTo2()
    {
        var config = new AppConfig();
        // Should default to 2 so transient network errors get retried
        Assert.Equal(2, config.GroqRetryCount);
    }

    // -------------------------------------------------------------------------
    // Dispose — verifies HttpClient is disposed
    // -------------------------------------------------------------------------

    [Fact]
    public void Dispose_DisposesHttpClient()
    {
        var handler = new FakeHttpMessageHandler();
        using var transcriber = CreateWithFakeHttp(handler);
        transcriber.Dispose();

        // Handler.Dispose was called during transcriber.Dispose
        Assert.Equal(handler.Times_Called, handler.SendAsync_CallCountAtDispose);
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose() { }
}

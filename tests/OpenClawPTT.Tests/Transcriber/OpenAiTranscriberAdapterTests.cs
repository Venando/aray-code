using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using OpenClawPTT.Transcriber;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Stability and error-handling tests for OpenAiTranscriberAdapter.
/// Verifies retry behavior, immediate-fail conditions, input guards,
/// and concurrency safety.
/// </summary>
public class OpenAiTranscriberAdapterTests : IDisposable
{
    private const string ValidApiKey = "test-key-123";
    private static readonly byte[] SmallWav = new byte[] { 0x52, 0x49, 0x46, 0x46 }; // "RIFF" — minimal WAV

    // ─── Helper: build an adapter with a mock handler that responds with the given function ───
    private static OpenAiTranscriberAdapter BuildAdapter(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(sendAsync);

        return new OpenAiTranscriberAdapter(
            apiKey: ValidApiKey,
            model: "whisper-1",
            maxRetries: 3,
            timeoutSeconds: 30,
            maxAudioSizeBytes: 10 * 1024 * 1024,
            httpHandler: handler.Object);
    }

    [Fact]
    public async Task TranscribeAsync_401Unauthorized_FailsImmediatelyNoRetry()
    {
        var callCount = 0;

        var adapter = BuildAdapter((_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("Invalid API key")
            });
        });

        var ex = await Assert.ThrowsAsync<TranscriberException>(
            () => adapter.TranscribeAsync(SmallWav));

        // Must be exactly 1 — no retries for auth errors
        Assert.Equal(1, callCount);
        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task TranscribeAsync_400BadRequest_FailsImmediatelyNoRetry()
    {
        var callCount = 0;

        var adapter = BuildAdapter((_, _) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("Invalid request format")
            });
        });

        var ex = await Assert.ThrowsAsync<TranscriberException>(
            () => adapter.TranscribeAsync(SmallWav));

        Assert.Equal(1, callCount);
        Assert.Contains("400", ex.Message);
    }

    [Fact]
    public async Task TranscribeAsync_NetworkTimeout_RetriesUpToLimit()
    {
        var callCount = 0;

        // A handler that throws TaskCanceledException wrapping TimeoutException
        var adapter = BuildAdapter((_, _) =>
        {
            Interlocked.Increment(ref callCount);
            var timeoutEx = new TimeoutException("The operation timed out.");
            throw new TaskCanceledException("HttpClient timeout", timeoutEx);
        });

        var ex = await Assert.ThrowsAsync<TranscriberException>(
            () => adapter.TranscribeAsync(SmallWav));

        // 1 initial attempt + 3 retries = 4 total calls
        Assert.Equal(4, callCount);
        Assert.Contains("timed out", ex.Message);
    }

    [Fact]
    public async Task TranscribeAsync_NullAudioBytes_ThrowsArgumentNullException()
    {
        var adapter = BuildAdapter((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"text\": \"should not be called\"}")
            }));

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => adapter.TranscribeAsync(null!));

        Assert.Equal("wavBytes", ex.ParamName);
    }

    [Fact]
    public async Task TranscribeAsync_EmptyAudioBytes_ThrowsArgumentNullException()
    {
        var adapter = BuildAdapter((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"text\": \"should not be called\"}")
            }));

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(
            () => adapter.TranscribeAsync(Array.Empty<byte>()));

        Assert.Equal("wavBytes", ex.ParamName);
    }

    [Fact]
    public async Task TranscribeAsync_VeryLargeAudioBytes_ThrowsArgumentOutOfRangeException()
    {
        // Use a small max size (100 bytes) for this test
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"text\": \"should not be called\"}")
            }));

        // maxAudioSizeBytes = 100 bytes
        var adapter = new OpenAiTranscriberAdapter(
            apiKey: ValidApiKey,
            model: "whisper-1",
            maxRetries: 3,
            timeoutSeconds: 30,
            maxAudioSizeBytes: 100,
            httpHandler: handler.Object);

        // Audio is 1 MB — way over the 100-byte limit
        var largeAudio = new byte[1024 * 1024];

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => adapter.TranscribeAsync(largeAudio));

        Assert.Equal("wavBytes", ex.ParamName);
    }

    [Fact]
    public async Task TranscribeAsync_ConcurrentCalls_AreSerialized()
    {
        var activeCallCount = 0;
        var maxConcurrent = 0;
        var callCount = 0;
        var callOrder = new System.Collections.Concurrent.ConcurrentQueue<int>();

        var adapter = BuildAdapter(async (req, ct) =>
        {
            var current = Interlocked.Increment(ref activeCallCount);
            Interlocked.Exchange(ref maxConcurrent, Math.Max(maxConcurrent, current));
            callOrder.Enqueue(Interlocked.Increment(ref callCount));

            // Simulate some async work
            await Task.Delay(20, ct).ConfigureAwait(false);

            Interlocked.Decrement(ref activeCallCount);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"text\": \"ok\"}")
            };
        });

        // Launch 3 calls concurrently
        var t1 = adapter.TranscribeAsync(SmallWav);
        var t2 = adapter.TranscribeAsync(SmallWav);
        var t3 = adapter.TranscribeAsync(SmallWav);

        await Task.WhenAll(t1, t2, t3);

        // All 3 completed successfully
        Assert.Equal(3, callCount);
        Assert.Equal(1, maxConcurrent); // Never more than 1 call active
    }

    [Fact]
    public async Task TranscribeAsync_503ThenSuccess_ReturnsResult()
    {
        var callCount = 0;

        var adapter = BuildAdapter((_, _) =>
        {
            var n = Interlocked.Increment(ref callCount);
            if (n <= 2)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("{\"error\": \"unavailable\"}")
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"text\": \"hello world\"}")
            });
        });

        var result = await adapter.TranscribeAsync(SmallWav);

        Assert.Equal(3, callCount); // 2 failures + 1 success
        Assert.Equal("hello world", result);
    }

    [Fact]
    public async Task TranscribeAsync_SuccessfulResponse_ReturnsTranscribedText()
    {
        var adapter = BuildAdapter((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"text\": \"testing one two three\"}")
            }));

        var result = await adapter.TranscribeAsync(SmallWav);

        Assert.Equal("testing one two three", result);
    }

    public void Dispose()
    {
        // Adapter is created per-test and disposed here to satisfy IDisposable
        // Note: each test creates its own adapter(s) — we rely on test class
        // not holding onto them. Add explicit disposal tracking if needed.
    }
}

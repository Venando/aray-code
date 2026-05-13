using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Transcriber;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Tests for <see cref="GroqTranscriberAdapter"/> — verifies cancellation passthrough.
/// </summary>
public class GroqTranscriberAdapterTests : IDisposable
{
    private const string ValidApiKey = "gsk_test_valid_key_12345";
    private static readonly byte[] SampleWav = new byte[] { 0x52, 0x49, 0x46, 0x46 }; // "RIFF"

    /// <summary>
    /// Creates a GroqTranscriberAdapter with a fake HttpClient that returns
    /// the given response.
    /// </summary>
    private static GroqTranscriberAdapter CreateWithFakeHttp(
        HttpMessageHandler handler,
        int retryCount = 0)
    {
        var adapter = new GroqTranscriberAdapter(ValidApiKey, retryCount: retryCount);

        // Reach into the inner GroqTranscriber and replace its HttpClient
        var innerField = typeof(GroqTranscriberAdapter)
            .GetField("_inner", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var inner = innerField.GetValue(adapter);

        var httpField = typeof(GroqTranscriber)
            .GetField("_http", BindingFlags.NonPublic | BindingFlags.Instance)!;
        httpField.SetValue(inner, new HttpClient(handler));

        return adapter;
    }

    [Fact]
    public async Task TranscribeAsync_PreCancelledToken_ThrowsOperationCanceledException()
    {
        var handler = new FakeHandler
        {
            ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("hello")
            }
        };

        using var transcriber = CreateWithFakeHttp(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Currently the adapter drops ct — this will throw GroqTranscriberException (401)
        // or something else instead of OperationCanceledException
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => transcriber.TranscribeAsync(SampleWav, ct: cts.Token));
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public HttpResponseMessage? ResponseToReturn { get; set; }
        public int Times_Called { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Times_Called++;
            if (ResponseToReturn == null)
                throw new InvalidOperationException("ResponseToReturn must be set.");
            return Task.FromResult(ResponseToReturn);
        }
    }

    public void Dispose() { }
}

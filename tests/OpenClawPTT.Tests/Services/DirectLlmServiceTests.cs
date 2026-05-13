using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Moq;
using OpenClawPTT.Services;

namespace OpenClawPTT.Tests;

public class DirectLlmServiceTests
{
    private static AppConfig MakeConfig()
    {
        return new AppConfig
        {
            DirectLlmUrl = "http://test-llm:11434",
            DirectLlmModelName = "test-model",
            DirectLlmToken = "test-token"
        };
    }

    private static Mock<IDirectLlmFailureTracker> MakeTracker()
    {
        var mock = new Mock<IDirectLlmFailureTracker>(MockBehavior.Loose);
        mock.Setup(t => t.Threshold).Returns(1);
        return mock;
    }

    private static HttpMessageHandlerStub MakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        return new HttpMessageHandlerStub(req => Task.FromResult(respond(req)));
    }

    private static DirectLlmService CreateService(
        AppConfig cfg,
        HttpMessageHandler handler,
        IDirectLlmFailureTracker? tracker = null)
    {
        // Use reflection or just call the constructor — we'll add the handler parameter
        return null!; // Placeholder until we know the constructor signature
    }

    // ── RED tests (will fail until retry + handler injection is implemented) ──

    [Fact]
    public void Constructor_WithHandler_UsesInjectedHandler()
    {
        var cfg = MakeConfig();
        var handler = MakeHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                id = "test",
                choices = new[] { new { message = new { role = "assistant", content = "hello" } } }
            }, options: new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            })
        });

        using var service = new DirectLlmService(cfg, handler: handler);
        Assert.NotNull(service);
    }

    [Fact]
    public async Task SendAsync_Success_RecordsSuccess()
    {
        // Arrange
        var cfg = MakeConfig();
        var tracker = MakeTracker();
        bool successRecorded = false;
        tracker.Setup(t => t.RecordSuccess()).Callback(() => successRecorded = true);

        var handler = MakeHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                id = "test",
                choices = new[] { new { message = new { role = "assistant", content = "hello" } } }
            }, options: new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            })
        });

        using var service = new DirectLlmService(cfg, tracker.Object, handler);
        var result = await service.SendAsync("hello");

        // Assert
        Assert.Equal("hello", result);
        Assert.True(successRecorded);
        tracker.Verify(t => t.RecordFailure(), Times.Never);
    }

    [Fact]
    public async Task SendAsync_Failure_RecordsFailure()
    {
        // Arrange
        var cfg = MakeConfig();
        var tracker = MakeTracker();
        bool failureRecorded = false;
        tracker.Setup(t => t.RecordFailure()).Callback(() => failureRecorded = true);

        var handler = MakeHandler(req => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        using var service = new DirectLlmService(cfg, tracker.Object, handler);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() => service.SendAsync("hello"));
        Assert.True(failureRecorded);
        tracker.Verify(t => t.RecordSuccess(), Times.Never);
    }

    [Fact]
    public async Task SendAsync_TransientFailure_RetriesThenSucceeds()
    {
        // Arrange
        var cfg = MakeConfig();
        var tracker = MakeTracker();
        int callCount = 0;

        var handler = MakeHandler(req =>
        {
            callCount++;
            if (callCount == 1)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    id = "test",
                    choices = new[] { new { message = new { role = "assistant", content = "retry worked" } } }
                }, options: new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                })
            };
        });

        using var service = new DirectLlmService(cfg, tracker.Object, handler);
        var result = await service.SendAsync("hello");

        // Assert: retried and got success
        Assert.Equal("retry worked", result);
        Assert.Equal(2, callCount);
        tracker.Verify(t => t.RecordSuccess(), Times.Once);
        tracker.Verify(t => t.RecordFailure(), Times.Never);
    }

    [Fact]
    public async Task SendAsync_TransientFailureAllRetriesExhausted_RecordsFailure()
    {
        // Arrange
        var cfg = MakeConfig();
        var tracker = MakeTracker();
        int callCount = 0;

        var handler = MakeHandler(req =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });

        using var service = new DirectLlmService(cfg, tracker.Object, handler);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() => service.SendAsync("hello"));
        Assert.Equal(2, callCount); // 1 original + 1 retry
        tracker.Verify(t => t.RecordFailure(), Times.Once);
        tracker.Verify(t => t.RecordSuccess(), Times.Never);
    }

    [Fact]
    public async Task SendAsync_NonTransientError_DoesNotRetry()
    {
        // Arrange
        var cfg = MakeConfig();
        var tracker = MakeTracker();
        int callCount = 0;

        var handler = MakeHandler(req =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        });

        using var service = new DirectLlmService(cfg, tracker.Object, handler);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() => service.SendAsync("hello"));
        Assert.Equal(1, callCount); // only 1 attempt, no retry
        tracker.Verify(t => t.RecordFailure(), Times.Once);
    }

    [Fact]
    public async Task SendAsync_Timeout_Retries()
    {
        // Arrange
        var cfg = MakeConfig();
        var tracker = MakeTracker();
        int callCount = 0;

        var handler = MakeHandler(req =>
        {
            callCount++;
            throw new TaskCanceledException("timeout");
        });

        using var service = new DirectLlmService(cfg, tracker.Object, handler);

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() => service.SendAsync("hello"));
        Assert.Equal(2, callCount); // retried once
        tracker.Verify(t => t.RecordFailure(), Times.Once);
    }

    [Fact]
    public async Task SendAsync_Cancellation_DoesNotRetry()
    {
        // Arrange
        var cfg = MakeConfig();
        var tracker = MakeTracker();
        int callCount = 0;

        using var cts = new CancellationTokenSource();

        var handler = MakeHandler(req =>
        {
            callCount++;
            cts.Cancel(); // trigger cancellation before/during request
            throw new OperationCanceledException(cts.Token);
        });

        using var service = new DirectLlmService(cfg, tracker.Object, handler);

        // Act & Assert
        // HttpClient wraps OperationCanceledException → TaskCanceledException
        await Assert.ThrowsAsync<TaskCanceledException>(() => service.SendAsync("hello", cts.Token));
        Assert.Equal(1, callCount); // no retry on cancellation
        tracker.Verify(t => t.RecordFailure(), Times.Never); // cancellation not a "failure"
    }
}

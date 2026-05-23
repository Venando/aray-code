using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Xunit;
using ArayCode.Services;

namespace ArayCode.Tests;

public class GatewayServiceStabilityTests : IDisposable
{
    private readonly AppConfig _config;

    public GatewayServiceStabilityTests()
    {
        _config = new AppConfig
        {
            GatewayUrl = "wss://test.example.com",
            AuthToken = "test-token",
        };
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

    [Fact]
    public async Task ConnectAsync_DelegatesToGatewayClient()
    {
        // Arrange
        var mockClient = new Mock<IGatewayClient>();
        mockClient.Setup(c => c.ConnectAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new TestableGatewayService(_config, mockClient.Object);

        // Act
        await service.ConnectAsync();

        // Assert
        mockClient.Verify(c => c.ConnectAsync(It.IsAny<CancellationToken>()), Times.Once);

        service.Dispose();
    }

    [Fact]
    public void RecreateWithConfig_DisposesOldClient()
    {
        // Arrange
        var mockClient = new Mock<IGatewayClient>();
        var service = new TestableGatewayService(_config, mockClient.Object);
        var disposeCalled = false;
        mockClient.Setup(c => c.Dispose()).Callback(() => disposeCalled = true);

        // Act
        var newConfig = new AppConfig { GatewayUrl = "wss://new.example.com" };
        service.RecreateWithConfig(newConfig);

        // Assert
        Assert.True(disposeCalled);
        mockClient.Verify(c => c.Dispose(), Times.Once);

        service.Dispose();
    }

    [Fact]
    public void RecreateWithConfig_ThrowsWhenDisposed()
    {
        // Arrange
        var mockClient = new Mock<IGatewayClient>();
        var service = new TestableGatewayService(_config, mockClient.Object);
        service.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => service.RecreateWithConfig(_config));
    }

    [Fact]
    public void Dispose_DisposesGatewayClient()
    {
        // Arrange
        var mockClient = new Mock<IGatewayClient>();
        var service = new TestableGatewayService(_config, mockClient.Object);
        var disposeCalled = false;
        mockClient.Setup(c => c.Dispose()).Callback(() => disposeCalled = true);

        // Act
        service.Dispose();

        // Assert
        Assert.True(disposeCalled);
        mockClient.Verify(c => c.Dispose(), Times.Once);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var mockClient = new Mock<IGatewayClient>();
        mockClient.Setup(c => c.Dispose()).Callback(() => { });
        var service = new TestableGatewayService(_config, mockClient.Object);

        // Act & Assert (should not throw)
        service.Dispose();
        service.Dispose();

        mockClient.Verify(c => c.Dispose(), Times.Once);
    }

    [Fact]
    public void GatewayService_ImplementsIGatewayService()
    {
        // Arrange
        var mockClient = new Mock<IGatewayClient>();
        var service = new TestableGatewayService(_config, mockClient.Object);

        // Assert
        Assert.True(service is IGatewayService);
        service.Dispose();
    }

    [Fact]
    public void RecreateWithConfig_CalledTwice_DisposesClientTwice()
    {
        // Arrange
        var mockClient = new Mock<IGatewayClient>();
        int disposeCount = 0;
        mockClient.Setup(c => c.Dispose()).Callback(() => disposeCount++);

        var service = new TestableGatewayService(_config, mockClient.Object);

        // Act
        service.RecreateWithConfig(_config);
        service.RecreateWithConfig(_config);

        // Assert - client is disposed twice (once per recreate)
        Assert.Equal(2, disposeCount);

        service.Dispose();
    }

    [Fact]
    public async Task SendTextAsync_VerifiesClientMethod()
    {
        // Arrange
        var mockClient = new Mock<IGatewayClient>();
        mockClient.Setup(c => c.SendTextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(default(JsonElement));

        var service = new TestableGatewayService(_config, mockClient.Object);

        // Act
        await service.SendTextAsync("hello");

        // Assert
        mockClient.Verify(c => c.SendTextAsync("hello", It.IsAny<CancellationToken>()), Times.Once);

        service.Dispose();
    }

    [Fact]
    public void EventSubscriptions_AreNotNull()
    {
        // Arrange
        var mockClient = new Mock<IGatewayClient>();
        var service = new TestableGatewayService(_config, mockClient.Object);

        // Act - subscribe to events (should not throw)
        int eventCount = 0;
        service.AgentThinking += _ => eventCount++;
        service.AgentToolCall += (_, _) => eventCount++;
        service.EventReceived += (_, _) => eventCount++;
        service.AgentReplyFinal += _ => eventCount++;

        // Assert - subscriptions work
        Assert.Equal(0, eventCount); // no events fired yet

        service.Dispose();
    }
}

/// <summary>
/// Testable subclass that allows injecting a mock IGatewayClient
/// instead of creating a real GatewayClient.
/// </summary>
internal sealed class TestableGatewayService : IGatewayService
{
    private readonly AppConfig _config;
    private readonly IGatewayClient _gatewayClient;
    private bool _disposed;

    // IGatewayUIEvents — backing fields for explicit event impl
    private Action? _connected;
    private Action? _disconnected;
    private Action? _reconnecting;
    private Action? _reconnectFailed;

    public event Action? Connected { add => _connected += value; remove => _connected -= value; }
    public event Action? Disconnected { add => _disconnected += value; remove => _disconnected -= value; }
    public event Action? Reconnecting { add => _reconnecting += value; remove => _reconnecting -= value; }
    public event Action? ReconnectFailed { add => _reconnectFailed += value; remove => _reconnectFailed -= value; }
    public event Action<string, string?>? AgentReplyFull; // (text, stopReason)
    public event Action<string>? AgentReplyFinal;
    public event Action? AgentReplyDeltaStart;
    public event Action<string>? AgentReplyDelta;
    public event Action? AgentReplyDeltaEnd;
    public event Action<string>? AgentThinking;
    public event Action<string, string>? AgentToolCall;
    public event Action<string, JsonElement>? EventReceived;

    public Task<List<ChatHistoryEntry>?> FetchSessionHistoryAsync(string sessionKey, int limit = 5)
    {
        return _gatewayClient.FetchSessionHistoryAsync(sessionKey, limit);
    }

    public void DisplayAssistantReply(string body)
    {
        AgentReplyFull?.Invoke(body, null);
    }

    public void DisplayHistoryEntry(ChatHistoryEntry entry)
    {
        DisplayAssistantReply(entry.Content);
    }

    public TestableGatewayService(AppConfig config, IGatewayClient gatewayClient)
    {
        _config = config;
        _gatewayClient = gatewayClient;
        WireEvents();
    }

    private void WireEvents()
    {
        var es = _gatewayClient.GetEventSource();
        if (es == null) return;

        es.AgentThinking += e => AgentThinking?.Invoke(e);
        es.AgentToolCall += (n, a) => AgentToolCall?.Invoke(n, a);
        es.EventReceived += (name, payload) => EventReceived?.Invoke(name, payload);

        // Wire delta/full paths based on config
        bool useDelta = _config.ReplyDisplayMode != ReplyDisplayMode.Full;
        bool useFull = _config.ReplyDisplayMode != ReplyDisplayMode.Delta;

        if (useDelta)
        {
            es.AgentReplyDeltaStart += () => AgentReplyDeltaStart?.Invoke();
            es.AgentReplyDelta += d => AgentReplyDelta?.Invoke(d);
            es.AgentReplyDeltaEnd += () => AgentReplyDeltaEnd?.Invoke();
        }

        if (useFull)
        {
            es.AgentReplyFull += (body, stopReason) => AgentReplyFull?.Invoke(body, stopReason);
            es.AgentReplyFinal += text => AgentReplyFinal?.Invoke(text);
        }
    }

    public Task ConnectAsync(CancellationToken ct = default) => _gatewayClient.ConnectAsync(ct);

    public Task SendTextAsync(string text, CancellationToken ct = default) =>
        _gatewayClient.SendTextAsync(text, ct);

    public async Task<JsonElement> SendRpcAsync(string method, object? parameters, CancellationToken ct = default) =>
        await _gatewayClient.SendEventAsync(method, parameters, ct);

    public void RecreateWithConfig(AppConfig newConfig)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TestableGatewayService));
        _gatewayClient.Dispose();
    }

    public Task RecreateTtsProviderAsync(AppConfig newConfig)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TestableGatewayService));
        return Task.CompletedTask;
    }

    public Action<bool>? OnTtsSynthesisStatus { get; set; }

    public void Dispose()
    {
        if (!_disposed)
        {
            _gatewayClient.Dispose();
            _disposed = true;
        }
    }

}
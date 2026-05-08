using System.Text.Json;
using Moq;
using OpenClawPTT;
using OpenClawPTT.Services;

namespace OpenClawPTT.Tests.Gateway;

/// <summary>
/// Tests for EventDispatcher, IEventDispatcher interface, and built-in handlers:
/// SessionMessageHandler and GatewayConnectionHandler.
/// </summary>
public class EventDispatcherTests
{
    private readonly Mock<IGatewayEventSource> _mockEvents;
    private readonly Mock<IColorConsole> _mockConsole;
    private readonly Mock<IRpcCaller> _mockRpc;
    private readonly AppConfig _cfg;
    private readonly IContentExtractor _contentExtractor;


    public EventDispatcherTests()
    {
        _mockEvents = new Mock<IGatewayEventSource>();
        _mockConsole = new Mock<IColorConsole>();
        _mockRpc = new Mock<IRpcCaller>();
        _cfg = new AppConfig
        {
            CustomDataDir = Path.GetTempPath(),
            GatewayUrl = "wss://127.0.0.1:9999/test",
            AuthToken = "test-token",
            RealTimeReplyOutput = false
        };
        _contentExtractor = new ContentExtractor();
    }

    // ─── EventDispatcher Core Tests ──────────────────────────────

    [Fact]
    public void RegisterHandler_DoesNotThrow()
    {
        var dispatcher = new EventDispatcher(_mockConsole.Object);
        var handler = new Mock<IEventHandler<GatewayEvent>>();
        var exception = Record.Exception(() => dispatcher.RegisterHandler(handler.Object));
        Assert.Null(exception);
    }

    [Fact]
    public async Task DispatchAsync_NoHandlers_DoesNotThrow()
    {
        var dispatcher = new EventDispatcher(_mockConsole.Object);
        var evt = new GatewayEvent("test", default);
        await dispatcher.DispatchAsync(evt);
    }

    [Fact]
    public async Task DispatchAsync_CallsRegisteredHandler()
    {
        var dispatcher = new EventDispatcher(_mockConsole.Object);
        bool called = false;
        var handlerMock = new Mock<IEventHandler<GatewayEvent>>();
        handlerMock.Setup(x => x.HandleAsync(It.IsAny<GatewayEvent>()))
            .Callback<GatewayEvent>(_ => called = true)
            .Returns(Task.CompletedTask);

        dispatcher.RegisterHandler(handlerMock.Object);
        await dispatcher.DispatchAsync(new GatewayEvent("test", default));

        Assert.True(called);
    }

    [Fact]
    public async Task DispatchAsync_CallsMultipleHandlersInOrder()
    {
        var dispatcher = new EventDispatcher(_mockConsole.Object);
        var calls = new List<int>();

        var handler1 = new Mock<IEventHandler<GatewayEvent>>();
        handler1.Setup(x => x.HandleAsync(It.IsAny<GatewayEvent>()))
            .Callback<GatewayEvent>(_ => calls.Add(1))
            .Returns(Task.CompletedTask);

        var handler2 = new Mock<IEventHandler<GatewayEvent>>();
        handler2.Setup(x => x.HandleAsync(It.IsAny<GatewayEvent>()))
            .Callback<GatewayEvent>(_ => calls.Add(2))
            .Returns(Task.CompletedTask);

        dispatcher.RegisterHandler(handler1.Object);
        dispatcher.RegisterHandler(handler2.Object);
        await dispatcher.DispatchAsync(new GatewayEvent("test", default));

        Assert.Equal(2, calls.Count);
        Assert.Equal(1, calls[0]);
        Assert.Equal(2, calls[1]);
    }

    [Fact]
    public void DispatchAndForget_DoesNotThrow()
    {
        var dispatcher = new EventDispatcher(_mockConsole.Object);
        var evt = new GatewayEvent("test", default);
        var exception = Record.Exception(() => dispatcher.DispatchAndForget(evt));
        Assert.Null(exception);
    }

    [Fact]
    public void DispatchAndForget_HandlerError_IsLoggedNotThrown()
    {
        var dispatcher = new EventDispatcher(_mockConsole.Object);
        var handlerMock = new Mock<IEventHandler<GatewayEvent>>();
        handlerMock.Setup(x => x.HandleAsync(It.IsAny<GatewayEvent>()))
            .ThrowsAsync(new InvalidOperationException("test error"));

        dispatcher.RegisterHandler(handlerMock.Object);
        dispatcher.DispatchAndForget(new GatewayEvent("test", default));

        // Give background task time to complete
        Thread.Sleep(100);

        _mockConsole.Verify(x => x.LogError("EventDispatcher", It.Is<string>(s => s.Contains("test error"))), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_DifferentEventTypes_AreIndependent()
    {
        var dispatcher = new EventDispatcher(_mockConsole.Object);
        bool gatewayCalled = false;
        bool sessionCalled = false;

        var gatewayHandler = new Mock<IEventHandler<GatewayEvent>>();
        gatewayHandler.Setup(x => x.HandleAsync(It.IsAny<GatewayEvent>()))
            .Callback<GatewayEvent>(_ => gatewayCalled = true)
            .Returns(Task.CompletedTask);

        var sessionHandler = new Mock<IEventHandler<SessionMessageEvent>>();
        sessionHandler.Setup(x => x.HandleAsync(It.IsAny<SessionMessageEvent>()))
            .Callback<SessionMessageEvent>(_ => sessionCalled = true)
            .Returns(Task.CompletedTask);

        dispatcher.RegisterHandler(gatewayHandler.Object);
        dispatcher.RegisterHandler(sessionHandler.Object);

        // Dispatch gateway event
        await dispatcher.DispatchAsync(new GatewayEvent("test", default));
        Assert.True(gatewayCalled);
        Assert.False(sessionCalled);
    }

    // ─── SessionMessageHandler Tests ─────────────────────────────

    [Fact]
    public async Task SessionMessageHandler_TextOnly_FiresAgentReplyFull()
    {
        string? captured = null;
        _mockEvents.Setup(x => x.RaiseAgentReplyFull(It.IsAny<string>()))
            .Callback<string>(t => captured = t);

        var handler = new SessionMessageHandler(_mockEvents.Object, _cfg, _contentExtractor, _mockConsole.Object);
        var payload = CreatePayload("{\"message\":{\"role\":\"assistant\", \"content\":[{\"type\":\"text\",\"text\":\"hello\"}]}}");
        await handler.HandleAsync(new SessionMessageEvent("session.message", payload));

        Assert.Equal("hello", captured);
    }

    [Fact]
    public async Task SessionMessageHandler_AudioOnly_FiresAgentReplyAudio()
    {
        string? captured = null;
        _mockEvents.Setup(x => x.RaiseAgentReplyAudio(It.IsAny<string>()))
            .Callback<string>(t => captured = t);

        var handler = new SessionMessageHandler(_mockEvents.Object, _cfg, _contentExtractor, _mockConsole.Object);
        var payload = CreatePayload("{\"message\":{\"role\":\"assistant\", \"content\":[{\"type\":\"audio\",\"audio\":\"voice data\"}]}}");
        await handler.HandleAsync(new SessionMessageEvent("session.message", payload));

        Assert.Equal("voice data", captured);
    }

    [Fact]
    public async Task SessionMessageHandler_NonAssistantRole_Ignores()
    {
        string? captured = null;
        _mockEvents.Setup(x => x.RaiseAgentReplyFull(It.IsAny<string>()))
            .Callback<string>(t => captured = t);

        var handler = new SessionMessageHandler(_mockEvents.Object, _cfg, _contentExtractor, _mockConsole.Object);
        var payload = CreatePayload("{\"message\":{\"role\":\"user\", \"content\":[{\"type\":\"text\",\"text\":\"hello\"}]}}");
        await handler.HandleAsync(new SessionMessageEvent("session.message", payload));

        Assert.Null(captured);
    }

    [Fact]
    public async Task SessionMessageHandler_NonArrayContent_DoesNotThrow()
    {
        var handler = new SessionMessageHandler(_mockEvents.Object, _cfg, _contentExtractor, _mockConsole.Object);
        var payload = CreatePayload("{\"message\":{\"role\":\"assistant\", \"content\":\"not an array\"}}");
        await handler.HandleAsync(new SessionMessageEvent("session.message", payload));
    }

    [Fact]
    public async Task SessionMessageHandler_ThinkingBlock_FiresAgentThinking()
    {
        string? capturedThinking = null;
        _mockEvents.Setup(x => x.RaiseAgentThinking(It.IsAny<string>()))
            .Callback<string>(t => capturedThinking = t);

        var handler = new SessionMessageHandler(_mockEvents.Object, _cfg, _contentExtractor, _mockConsole.Object);
        var payload = CreatePayload("{\"message\":{\"role\":\"assistant\", \"content\":[{\"type\":\"thinking\",\"thinking\":\" 分析中\"}]}}");
        await handler.HandleAsync(new SessionMessageEvent("session.message", payload));

        Assert.Equal(" 分析中", capturedThinking);
    }

    [Fact]
    public async Task SessionMessageHandler_ToolCallBlock_FiresAgentToolCall()
    {
        string? capturedName = null, capturedArgs = null;
        _mockEvents.Setup(x => x.RaiseAgentToolCall(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((n, a) => { capturedName = n; capturedArgs = a; });

        var handler = new SessionMessageHandler(_mockEvents.Object, _cfg, _contentExtractor, _mockConsole.Object);
        var payload = CreatePayload("{\"message\":{\"role\":\"assistant\", \"content\":[{\"type\":\"toolCall\",\"name\":\"read\",\"arguments\":\"{\\\"path\\\":\\\"a.md\\\"}\"}]}}");
        await handler.HandleAsync(new SessionMessageEvent("session.message", payload));

        Assert.Equal("read", capturedName);
        Assert.Contains("path", capturedArgs ?? "");
    }

    [Fact]
    public async Task SessionMessageHandler_AgentStream_DoesNothingInBatchMode()
    {
        var fired = false;
        _mockEvents.Setup(x => x.RaiseAgentReplyDeltaStart()).Callback(() => fired = true);

        var handler = new SessionMessageHandler(_mockEvents.Object, _cfg, _contentExtractor, _mockConsole.Object);
        var payload = CreatePayload("{\"data\":{\"phase\":\"start\"}}");
        await handler.HandleAsync(new SessionMessageEvent("agent", payload));

        Assert.False(fired); // batch mode, no delta
    }

    [Fact]
    public async Task SessionMessageHandler_AgentStream_DoesWorkInRealtimeMode()
    {
        var realtimeCfg = new AppConfig
        {
            CustomDataDir = Path.GetTempPath(),
            GatewayUrl = "wss://127.0.0.1:9999/test",
            AuthToken = "test-token",
            RealTimeReplyOutput = true
        };

        var startFired = false;
        _mockEvents.Setup(x => x.RaiseAgentReplyDeltaStart()).Callback(() => startFired = true);
        string? capturedDelta = null;
        _mockEvents.Setup(x => x.RaiseAgentReplyDelta(It.IsAny<string>()))
            .Callback<string>(t => capturedDelta = t);

        var handler = new SessionMessageHandler(_mockEvents.Object, realtimeCfg, _contentExtractor, _mockConsole.Object);
        var payload = CreatePayload("{\"data\":{\"phase\":\"start\",\"delta\":\"hello\"}}");
        await handler.HandleAsync(new SessionMessageEvent("agent", payload));

        Assert.True(startFired);
        Assert.Equal("hello", capturedDelta);
    }

    [Fact]
    public async Task SessionMessageHandler_ChatFinal_FiresDeltaEvents()
    {
        var startFired = false;
        _mockEvents.Setup(x => x.RaiseAgentReplyDeltaStart()).Callback(() => startFired = true);

        var handler = new SessionMessageHandler(_mockEvents.Object, _cfg, _contentExtractor, _mockConsole.Object);
        var payload = CreatePayload("{\"state\":\"final\",\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"final text\"}]}}");
        await handler.HandleAsync(new SessionMessageEvent("chat", payload));

        Assert.True(startFired);
    }

    // ─── GatewayConnectionHandler Tests ──────────────────────────

    [Fact]
    public async Task GatewayConnectionHandler_Connected_Logs()
    {
        var handler = new GatewayConnectionHandler(_mockConsole.Object);
        await handler.HandleAsync(new GatewayConnectedEvent(new Uri("wss://example.com")));

        _mockConsole.Verify(x => x.Log("gateway", It.Is<string>(s => s.Contains("Connected"))), Times.Once);
    }

    [Fact]
    public async Task GatewayConnectionHandler_Disconnected_Logs()
    {
        var handler = new GatewayConnectionHandler(_mockConsole.Object);
        await handler.HandleAsync(new GatewayDisconnectedEvent("test reason"));

        _mockConsole.Verify(x => x.Log("gateway", It.Is<string>(s => s.Contains("Disconnected"))), Times.Once);
    }

    // ─── Helpers ──────────────────────────────────────────────────

    private static JsonElement CreatePayload(string payloadJson)
    {
        return JsonDocument.Parse(payloadJson).RootElement.Clone();
    }
}

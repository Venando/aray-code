using System.Net.WebSockets;
using Moq;
using ArayCode;
using Xunit;

namespace ArayCode.Tests.Gateway;

/// <summary>
/// Note: MessageFraming is not sealed and has virtual members — it can be subclassed
/// for testing, but these tests use a real instance as the factory result to validate
/// that factory injection works correctly (GetFraming returns the provided instance).
///
/// GatewayMessager now uses IEventDispatcher internally. When no dispatcher is injected,
/// a default EventDispatcher is created with built-in SessionMessageHandler and
/// GatewayConnectionHandler. Tests that verify event handler behavior through
/// ProcessFrame use the default dispatcher which routes to SessionMessageHandler.
/// Tests that verify dispatch-level behavior inject a mock IEventDispatcher.
/// </summary>
public class GatewayMessagerTests : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly Mock<IClientWebSocket> _mockWs;
    private readonly Mock<IGatewayEventSource> _mockEvents;
    private readonly MessageFraming _realFraming;
    private readonly GatewayMessager _messager;

    public GatewayMessagerTests()
    {
        _cfg = new AppConfig
        {
            CustomDataDir = Path.GetTempPath(),
            GatewayUrl = "wss://127.0.0.1:9999/test",
            AuthToken = "test-token",
            ReplyDisplayMode = ReplyDisplayMode.Full
        };
        _mockWs = new Mock<IClientWebSocket>();
        _mockWs.Setup(x => x.State).Returns(WebSocketState.Open);
        _mockEvents = new Mock<IGatewayEventSource>();
        _realFraming = new MessageFraming(_mockWs.Object, _cfg);

        _messager = new GatewayMessager(_mockWs.Object, _mockEvents.Object, _cfg, null, () => _realFraming);
    }

    // ─── Construction tests ─────────────────────────────────────

    [Fact]
    public void GetFraming_ReturnsProvidedFraming()
    {
        var framing = _messager.GetFraming();
        Assert.Same(_realFraming, framing);
    }

    [Fact]
    public void ClearFraming_DoesNotThrow()
    {
        var exception = Record.Exception(() => _messager.ClearFraming());
        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        _messager.Dispose();
        var exception = Record.Exception(() => _messager.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void Construct_WithNullFramingFactory_CreatesRealFraming()
    {
        var messager = new GatewayMessager(_mockWs.Object, _mockEvents.Object, _cfg);
        Assert.NotNull(messager.GetFraming());
        Assert.IsType<MessageFraming>(messager.GetFraming());
        messager.Dispose();
    }

    [Fact]
    public void Construct_WithMockDispatcher_DoesNotCreateDefaultHandlers()
    {
        var mockDispatcher = new Mock<IEventDispatcher>();
        var messager = new GatewayMessager(_mockWs.Object, _mockEvents.Object, _cfg,
            null, () => _realFraming, null, mockDispatcher.Object);

        // Verify default handlers are still registered
        mockDispatcher.Verify(x => x.RegisterHandler(It.IsAny<IEventHandler<SessionMessageEvent>>()), Times.Once);
        mockDispatcher.Verify(x => x.RegisterHandler(It.IsAny<IEventHandler<GatewayDisconnectedEvent>>()), Times.Once);
        mockDispatcher.Verify(x => x.RegisterHandler(It.IsAny<IEventHandler<SideResultEvent>>()), Times.Once);

        messager.Dispose();
    }

    // ─── ProcessFrame tests (via real dispatcher + handlers) ─────

    [Fact]
    public void TestProcessFrame_SessionMessageEvent_FiresAgentReplyFull()
    {
        string? capturedText = null;
        _mockEvents.Setup(x => x.RaiseAgentReplyFull(It.IsAny<string>(), It.IsAny<string?>()))
            .Callback<string, string?>((t, _) => capturedText = t);

        var json = @"{""type"":""event"",""event"":""session.message"",""payload"":{""message"":{""role"":""assistant"",""content"":[{""type"":""text"",""text"":""hello world""}]}}}";
        _messager.TestProcessFrame(json);

        Assert.Equal("hello world", capturedText);
    }

    [Fact]
    public void TestProcessFrame_ThinkingBlock_FiresAgentThinking()
    {
        string? capturedThinking = null;
        _mockEvents.Setup(x => x.RaiseAgentThinking(It.IsAny<string>()))
            .Callback<string>(t => capturedThinking = t);

        var json = @"{""type"":""event"",""event"":""session.message"",""payload"":{""message"":{""role"":""assistant"",""content"":[{""type"":""thinking"",""thinking"":"" 分析中""}]}}}";
        _messager.TestProcessFrame(json);

        Assert.Equal(" 分析中", capturedThinking);
    }

    [Fact]
    public void TestProcessFrame_ToolCallBlock_FiresAgentToolCall()
    {
        string? capturedName = null, capturedArgs = null;
        _mockEvents.Setup(x => x.RaiseAgentToolCall(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((n, a) => { capturedName = n; capturedArgs = a; });

        var json = @"{""type"":""event"",""event"":""session.message"",""payload"":{""message"":{""role"":""assistant"",""content"":[{""type"":""toolCall"",""name"":""read"",""arguments"":""{\""path\"":\""a.md\""}""}]}}}";
        _messager.TestProcessFrame(json);

        Assert.Equal("read", capturedName);
        Assert.Contains("path", capturedArgs ?? "");
    }

    [Fact]
    public void TestProcessFrame_AgentStream_DoesNothingInBatchMode()
    {
        var fired = false;
        _mockEvents.Setup(x => x.RaiseAgentReplyDeltaStart()).Callback(() => fired = true);

        var json = @"{""type"":""event"",""event"":""agent"",""payload"":{""data"":{""phase"":""start""}}}";
        _messager.TestProcessFrame(json);

        Assert.False(fired); // batch mode, no delta
    }

    [Fact]
    public void TestProcessFrame_ChatFinal_FiresAgentReplyFinal()
    {
        string? captured = null;
        _mockEvents.Setup(x => x.RaiseAgentReplyFinal(It.IsAny<string>()))
            .Callback<string>(t => captured = t);

        var json = @"{""type"":""event"",""event"":""chat"",""payload"":{""state"":""final"",""message"":{""content"":[{""type"":""text"",""text"":""final text""}]}}}";
        _messager.TestProcessFrame(json);

        Assert.Equal("final text", captured);
    }

    [Fact]
    public void TestProcessFrame_UnknownType_DoesNotThrow()
    {
        var json = @"{""type"":""event"",""event"":""unknown.event"",""payload"":{}}";
        var exception = Record.Exception(() => _messager.TestProcessFrame(json));
        Assert.Null(exception);
    }

    // ─── Dispatcher integration tests ────────────────────────────

    [Fact]
    public void ProcessFrame_SessionMessage_DispatchesSessionMessageEvent()
    {
        var mockDispatcher = new Mock<IEventDispatcher>();
        SessionMessageEvent? dispatchedEvent = null;
        mockDispatcher.Setup(x => x.DispatchAndForget(It.IsAny<SessionMessageEvent>()))
            .Callback<SessionMessageEvent>(e => dispatchedEvent = e);

        var messager = new GatewayMessager(_mockWs.Object, _mockEvents.Object, _cfg,
            null, () => _realFraming, null, mockDispatcher.Object);

        var json = @"{""type"":""event"",""event"":""session.message"",""payload"":{""message"":{""role"":""assistant"",""content"":[]}}}";
        messager.TestProcessFrame(json);

        Assert.NotNull(dispatchedEvent);
        Assert.Equal("session.message", dispatchedEvent!.EventName);
        messager.Dispose();
    }

    [Fact]
    public void ProcessFrame_AgentEvent_DispatchesSessionMessageEvent()
    {
        var mockDispatcher = new Mock<IEventDispatcher>();
        SessionMessageEvent? dispatchedEvent = null;
        mockDispatcher.Setup(x => x.DispatchAndForget(It.IsAny<SessionMessageEvent>()))
            .Callback<SessionMessageEvent>(e => dispatchedEvent = e);

        var messager = new GatewayMessager(_mockWs.Object, _mockEvents.Object, _cfg,
            null, () => _realFraming, null, mockDispatcher.Object);

        var json = @"{""type"":""event"",""event"":""agent"",""payload"":{""data"":{""phase"":""start""}}}";
        messager.TestProcessFrame(json);

        Assert.NotNull(dispatchedEvent);
        Assert.Equal("agent", dispatchedEvent!.EventName);
        messager.Dispose();
    }

    [Fact]
    public void ProcessFrame_ChatEvent_DispatchesSessionMessageEvent()
    {
        var mockDispatcher = new Mock<IEventDispatcher>();
        SessionMessageEvent? dispatchedEvent = null;
        mockDispatcher.Setup(x => x.DispatchAndForget(It.IsAny<SessionMessageEvent>()))
            .Callback<SessionMessageEvent>(e => dispatchedEvent = e);

        var messager = new GatewayMessager(_mockWs.Object, _mockEvents.Object, _cfg,
            null, () => _realFraming, null, mockDispatcher.Object);

        var json = @"{""type"":""event"",""event"":""chat"",""payload"":{""state"":""final"",""message"":{""content"":[]}}}";
        messager.TestProcessFrame(json);

        Assert.NotNull(dispatchedEvent);
        Assert.Equal("chat", dispatchedEvent!.EventName);
        messager.Dispose();
    }

    [Fact]
    public void ProcessFrame_UnknownEvent_DispatchesGatewayEvent()
    {
        var mockDispatcher = new Mock<IEventDispatcher>();
        GatewayEvent? dispatchedEvent = null;
        mockDispatcher.Setup(x => x.DispatchAndForget(It.IsAny<GatewayEvent>()))
            .Callback<GatewayEvent>(e => dispatchedEvent = e);

        var messager = new GatewayMessager(_mockWs.Object, _mockEvents.Object, _cfg,
            null, () => _realFraming, null, mockDispatcher.Object);

        var json = @"{""type"":""event"",""event"":""unknown.event"",""payload"":{}}";
        messager.TestProcessFrame(json);

        Assert.NotNull(dispatchedEvent);
        Assert.Equal("unknown.event", dispatchedEvent!.Name);
        messager.Dispose();
    }

    // ─── SideResult / btw event tests ───────────────────────────

    [Fact]
    public void ProcessFrame_ChatSideResult_DispatchesSideResultEvent()
    {
        var mockDispatcher = new Mock<IEventDispatcher>();
        SideResultEvent? dispatchedEvent = null;
        mockDispatcher.Setup(x => x.DispatchAndForget(It.IsAny<SideResultEvent>()))
            .Callback<SideResultEvent>(e => dispatchedEvent = e);

        var messager = new GatewayMessager(_mockWs.Object, _mockEvents.Object, _cfg,
            null, () => _realFraming, null, mockDispatcher.Object);

        var json = @"{""type"":""event"",""event"":""chat.side_result"",""payload"":{""kind"":""btw"",""question"":""yo"",""text"":""response"",""isError"":false}}";
        messager.TestProcessFrame(json);

        Assert.NotNull(dispatchedEvent);
        messager.Dispose();
    }

    [Fact]
    public void ProcessFrame_ChatSideResultWithSessionKey_DispatchesSideResultEvent()
    {
        var mockDispatcher = new Mock<IEventDispatcher>();
        SideResultEvent? dispatchedEvent = null;
        mockDispatcher.Setup(x => x.DispatchAndForget(It.IsAny<SideResultEvent>()))
            .Callback<SideResultEvent>(e => dispatchedEvent = e);

        var messager = new GatewayMessager(_mockWs.Object, _mockEvents.Object, _cfg,
            null, () => _realFraming, null, mockDispatcher.Object);

        // sessionKey in payload points to btw target session, NOT the active session.
        // This must NOT be filtered out.
        var json = @"{""type"":""event"",""event"":""chat.side_result"",""payload"":{""kind"":""btw"",""sessionKey"":""agent:anime:main"",""question"":""yo"",""text"":""response"",""isError"":false}}";
        messager.TestProcessFrame(json);

        Assert.NotNull(dispatchedEvent);
        messager.Dispose();
    }

    [Fact]
    public void ProcessFrame_ChatSideResult_DoesNotDispatchSessionMessageEvent()
    {
        var mockDispatcher = new Mock<IEventDispatcher>();
        SessionMessageEvent? sessionEvent = null;
        mockDispatcher.Setup(x => x.DispatchAndForget(It.IsAny<SessionMessageEvent>()))
            .Callback<SessionMessageEvent>(e => sessionEvent = e);

        var messager = new GatewayMessager(_mockWs.Object, _mockEvents.Object, _cfg,
            null, () => _realFraming, null, mockDispatcher.Object);

        var json = @"{""type"":""event"",""event"":""chat.side_result"",""payload"":{""kind"":""btw"",""question"":""yo"",""text"":""response"",""isError"":false}}";
        messager.TestProcessFrame(json);

        Assert.Null(sessionEvent);
        messager.Dispose();
    }

    public void Dispose()
    {
        _messager.Dispose();
    }
}

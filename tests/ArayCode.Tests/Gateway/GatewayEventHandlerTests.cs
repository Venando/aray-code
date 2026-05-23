using System.Text.Json;
using Moq;
using ArayCode.Services;

namespace ArayCode.Tests.Gateway;

public class GatewayEventHandlerTests
{
    private readonly Mock<IColorConsole> _mockConsole;
    private readonly GatewayEventHandler _handler;

    public GatewayEventHandlerTests()
    {
        _mockConsole = new Mock<IColorConsole>();
        _handler = new GatewayEventHandler(_mockConsole.Object);
    }

    [Fact]
    public async Task HandleAsync_QuietEvent_DoesNothing()
    {
        var evt = new GatewayEvent("heartbeat", JsonDocument.Parse("{}").RootElement.Clone());
        await _handler.HandleAsync(evt);

        _mockConsole.Verify(c => c.PrintWarning(It.IsAny<string>()), Times.Never);
        _mockConsole.Verify(c => c.Log(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<LogLevel>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_QuietEvent_Presence_DoesNothing()
    {
        var evt = new GatewayEvent("presence", JsonDocument.Parse("{}").RootElement.Clone());
        await _handler.HandleAsync(evt);

        _mockConsole.Verify(c => c.PrintWarning(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_NoteworthyEvent_Error_PrintsWarning()
    {
        var payload = JsonDocument.Parse("{\"message\":\"test error\"}").RootElement.Clone();
        var evt = new GatewayEvent("error", payload);

        await _handler.HandleAsync(evt);

        _mockConsole.Verify(c => c.PrintWarning(It.Is<string>(s => s.Contains("error") && s.Contains("test error"))), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_NoteworthyEvent_Warning_PrintsWarning()
    {
        var payload = JsonDocument.Parse("{\"message\":\"quota at 90%\"}").RootElement.Clone();
        var evt = new GatewayEvent("warning", payload);

        await _handler.HandleAsync(evt);

        _mockConsole.Verify(c => c.PrintWarning(It.Is<string>(s => s.Contains("warning") && s.Contains("quota at 90%"))), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_NoteworthyEvent_ModelFailover_PrintsWarning()
    {
        var payload = JsonDocument.Parse("{\"message\":\"fallback to deepseek\"}").RootElement.Clone();
        var evt = new GatewayEvent("model.failover", payload);

        await _handler.HandleAsync(evt);

        _mockConsole.Verify(c => c.PrintWarning(It.Is<string>(s => s.Contains("model.failover") && s.Contains("fallback"))), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_UnknownEvent_LogsDebug()
    {
        var payload = JsonDocument.Parse("{\"foo\":\"bar\"}").RootElement.Clone();
        var evt = new GatewayEvent("some.custom.event", payload);

        await _handler.HandleAsync(evt);

        _mockConsole.Verify(c => c.PrintWarning(It.IsAny<string>()), Times.Never);
        _mockConsole.Verify(c => c.Log("gateway", It.Is<string>(s => s.Contains("some.custom.event")), LogLevel.Debug), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ExtractMessage_FromMessageField()
    {
        var payload = JsonDocument.Parse("{\"message\":\"hello world\"}").RootElement.Clone();
        var evt = new GatewayEvent("error", payload);

        await _handler.HandleAsync(evt);

        _mockConsole.Verify(c => c.PrintWarning(It.Is<string>(s => s.Contains("hello world"))), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ExtractMessage_FromTextField()
    {
        var payload = JsonDocument.Parse("{\"text\":\"some text\"}").RootElement.Clone();
        var evt = new GatewayEvent("error", payload);

        await _handler.HandleAsync(evt);

        _mockConsole.Verify(c => c.PrintWarning(It.Is<string>(s => s.Contains("some text"))), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ExtractMessage_FromErrorField()
    {
        var payload = JsonDocument.Parse("{\"error\":\"something failed\"}").RootElement.Clone();
        var evt = new GatewayEvent("error", payload);

        await _handler.HandleAsync(evt);

        _mockConsole.Verify(c => c.PrintWarning(It.Is<string>(s => s.Contains("something failed"))), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_NonObjectPayload_ReturnsEmpty()
    {
        var payload = JsonDocument.Parse("\"just a string\"").RootElement.Clone();
        var evt = new GatewayEvent("error", payload);

        await _handler.HandleAsync(evt);

        _mockConsole.Verify(c => c.PrintWarning(It.Is<string>(s => s == "Gateway: error — ")), Times.Once);
    }
}

using ArayCode.Services;
using Xunit;
using Moq;

namespace ArayCode.Tests;

public class GatewayServiceTests
{
    private static AgentOutputCoordinator CreateCoordinator(AppConfig cfg)
    {
        var console = CreateMockConsole();
        return new AgentOutputCoordinator(
            new ReplyStreamCoordinator(cfg, console),
            new ToolDisplayHandler(cfg.RightMarginIndent, console.GetStreamShellHost()),
            new ThinkingDisplayHandler(cfg, console.GetStreamShellHost()),
            audioHandler: null);
    }

    private static IColorConsole CreateMockConsole() => new Mock<IColorConsole>().Object;

    private static AppConfig CreateConfig() => new()
    {
        GatewayUrl = "wss://test.example.com",
        AuthToken = "test-token",
    };

    [Fact]
    public void GatewayService_Constructs_WithoutThrowing()
    {
        var service = new GatewayService(CreateConfig(), CreateMockConsole(), CreateCoordinator(CreateConfig()));
        Assert.NotNull(service);
        service.Dispose();
    }

    [Fact]
    public void GatewayService_ImplementsIGatewayService()
    {
        IGatewayService service = new GatewayService(CreateConfig(), CreateMockConsole(), CreateCoordinator(CreateConfig()));
        Assert.IsType<GatewayService>(service);
        service.Dispose();
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var service = new GatewayService(CreateConfig(), CreateMockConsole(), CreateCoordinator(CreateConfig()));

        service.Dispose();
        var exception = Record.Exception(() => service.Dispose());
        Assert.Null(exception);
    }
}

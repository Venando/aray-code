using Xunit;
using Moq;
using ArayCode.Services;


namespace ArayCode.Tests;

[Collection("AgentRegistryCollection")]
public class ReplyStreamCoordinatorTests
{
    static ReplyStreamCoordinatorTests()
    {
        AgentSettingsPersistenceLegacy.Initialize(Mock.Of<IAgentSettingsPersistence>());
        AgentRegistry.SetAgents(new[]
        {
            new AgentInfo
            {
                AgentId = "test",
                Name = "TestAgent",
                SessionKey = "test-session",
                IsDefault = true
            }
        });
    }


    private static Mock<IColorConsole> CreateMockConsole()
    {
        var mock = new Mock<IColorConsole>(MockBehavior.Loose);
        // Allow GetStreamShellHost to return null by default
        mock.Setup(x => x.GetStreamShellHost()).Returns((IStreamShellHost?)null);
        return mock;
    }

    private ReplyStreamCoordinator CreateCoordinator(AppConfig? config = null)
    {
        config ??= new AppConfig();
        return new ReplyStreamCoordinator(config, CreateMockConsole().Object);
    }

    [Fact]
    public void OnDeltaStart_ResetsState()
    {
        var c = CreateCoordinator();
        Assert.False(c.IsDeltaStarted);
        Assert.Empty(c.AccumulatedText);

        c.OnDeltaStart();
        Assert.True(c.IsDeltaStarted);

        c.OnDeltaEnd();
        Assert.False(c.IsDeltaStarted);
        Assert.Empty(c.AccumulatedText);
    }

    [Fact]
    public void OnDelta_AccumulatesText()
    {
        var c = CreateCoordinator(new AppConfig { EnableWordWrap = false });
        c.OnDeltaStart();
        c.OnDelta("Hello ");
        c.OnDelta("World");
        Assert.Equal("Hello World", c.AccumulatedText);
    }

    [Fact]
    public void OnDelta_BeforeStart_IsNoOp()
    {
        var c = CreateCoordinator();
        c.OnDelta("should not be stored");
        Assert.Empty(c.AccumulatedText);
    }

    [Fact]
    public void OnDeltaEnd_WithoutStart_IsNoOp()
    {
        var c = CreateCoordinator();
        c.OnDeltaEnd();
        Assert.False(c.IsDeltaStarted);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var c = CreateCoordinator();
        c.Dispose();
        c.Dispose(); // no exception
    }

    [Fact]
    public void OnFullReply_UsesMarkdownConversion()
    {
        var config = new AppConfig { EnableWordWrap = false };
        var consoleMock = CreateMockConsole();
        consoleMock.Setup(x => x.PrintAgentReplyWithMarkdown(
                It.IsAny<string>(), It.IsAny<string>()))
            .Verifiable();
        consoleMock.Setup(x => x.Log(It.IsAny<string>(), It.IsAny<string>()));

        var c = new ReplyStreamCoordinator(config, consoleMock.Object);

        c.OnFullReply("Hello **world**");

        consoleMock.Verify(
            x => x.PrintAgentReplyWithMarkdown(It.IsAny<string>(), It.IsAny<string>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public void OnDelta_UsesPrintAgentReplyDelta_WhenWordWrapDisabled()
    {
        var config = new AppConfig { EnableWordWrap = false };
        var consoleMock = CreateMockConsole();
        consoleMock.Setup(x => x.PrintAgentReplyDelta(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Verifiable();
        consoleMock.Setup(x => x.Log(It.IsAny<string>(), It.IsAny<string>()));

        var c = new ReplyStreamCoordinator(config, consoleMock.Object);
        c.OnDeltaStart();
        c.OnDelta("test");
        c.OnDeltaEnd();

        consoleMock.Verify(
            x => x.PrintAgentReplyDelta(It.IsAny<string>(), "test", It.IsAny<string>()),
            Times.Once);
    }
}

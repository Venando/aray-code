using System;
using Xunit;
using Moq;
using OpenClawPTT.Services;

namespace OpenClawPTT.Tests;

public class AgentOutputCoordinatorTests
{
    /// <summary>
    /// Creates a coordinator with EnableWordWrap=false to avoid AgentReplyFormatter
    /// NRE in tests that don't exercise word wrapping.
    /// </summary>
    private static AgentOutputCoordinator CreateCoordinator(AppConfig? config = null)
    {
        config ??= new AppConfig
        {
            AudioResponseMode = "text-only",
            EnableWordWrap = false
        };
        var console = new Mock<IColorConsole>().Object;
        return new AgentOutputCoordinator(
            new ReplyStreamCoordinator(config, console),
            new ToolDisplayHandler(config.RightMarginIndent, console.GetStreamShellHost()),
            new ThinkingDisplayHandler(config, console.GetStreamShellHost()),
            audioHandler: null);
    }

    [Fact]
    public void AttachToService_WiresAllEvents()
    {
        var serviceMock = new Mock<IGatewayUIEvents>();
        var coordinator = CreateCoordinator();

        coordinator.AttachToService(serviceMock.Object);

        serviceMock.VerifyAdd(x => x.AgentReplyFull += It.IsAny<Action<string>>(), Times.Once);
        serviceMock.VerifyAdd(x => x.AgentThinking += It.IsAny<Action<string>>(), Times.Once);
        serviceMock.VerifyAdd(x => x.AgentToolCall += It.IsAny<Action<string, string>>(), Times.Once);
        serviceMock.VerifyAdd(x => x.AgentReplyDeltaStart += It.IsAny<Action>(), Times.Once);
        serviceMock.VerifyAdd(x => x.AgentReplyDelta += It.IsAny<Action<string>>(), Times.Once);
        serviceMock.VerifyAdd(x => x.AgentReplyDeltaEnd += It.IsAny<Action>(), Times.Once);
        serviceMock.VerifyAdd(x => x.AgentReplyAudio += It.IsAny<Action<string>>(), Times.Once);
    }

    [Fact]
    public void DetachFromService_UnsubscribesAllEvents()
    {
        var serviceMock = new Mock<IGatewayUIEvents>();
        var coordinator = CreateCoordinator();

        coordinator.AttachToService(serviceMock.Object);
        coordinator.DetachFromService(serviceMock.Object);

        serviceMock.VerifyRemove(x => x.AgentReplyFull -= It.IsAny<Action<string>>(), Times.Once);
        serviceMock.VerifyRemove(x => x.AgentThinking -= It.IsAny<Action<string>>(), Times.Once);
        serviceMock.VerifyRemove(x => x.AgentToolCall -= It.IsAny<Action<string, string>>(), Times.Once);
        serviceMock.VerifyRemove(x => x.AgentReplyDeltaStart -= It.IsAny<Action>(), Times.Once);
        serviceMock.VerifyRemove(x => x.AgentReplyDelta -= It.IsAny<Action<string>>(), Times.Once);
        serviceMock.VerifyRemove(x => x.AgentReplyDeltaEnd -= It.IsAny<Action>(), Times.Once);
        serviceMock.VerifyRemove(x => x.AgentReplyAudio -= It.IsAny<Action<string>>(), Times.Once);
    }

    [Fact]
    public void OnAgentReplyFull_DoesNotThrow()
    {
        var coordinator = CreateCoordinator();
        var ex = Record.Exception(() => coordinator.OnAgentReplyFull("test"));
        Assert.Null(ex);
    }

    [Fact]
    public void OnAgentReplyDeltaStart_DoesNotThrow()
    {
        var coordinator = CreateCoordinator();
        var ex = Record.Exception(() => coordinator.OnAgentReplyDeltaStart());
        Assert.Null(ex);
    }

    [Fact]
    public void OnAgentReplyDelta_DoesNotThrow()
    {
        var coordinator = CreateCoordinator();
        coordinator.OnAgentReplyDeltaStart();
        var ex = Record.Exception(() => coordinator.OnAgentReplyDelta("chunk"));
        Assert.Null(ex);
    }

    [Fact]
    public void OnAgentReplyDeltaEnd_DoesNotThrow()
    {
        var coordinator = CreateCoordinator();

        coordinator.OnAgentReplyDeltaStart();
        coordinator.OnAgentReplyDelta("chunk");

        var ex = Record.Exception(() => coordinator.OnAgentReplyDeltaEnd());
        Assert.Null(ex);
    }

    [Fact]
    public void OnAgentThinking_DoesNotThrow()
    {
        var coordinator = CreateCoordinator();
        var ex = Record.Exception(() => coordinator.OnAgentThinking("deep thoughts"));
        Assert.Null(ex);
    }

    [Fact]
    public void OnAgentToolCall_DoesNotThrow()
    {
        var coordinator = CreateCoordinator();
        var ex = Record.Exception(() => coordinator.OnAgentToolCall("read", "{}"));
        Assert.Null(ex);
    }

    [Fact]
    public void SetAudioHandler_WhenCalled_ReplacesHandler()
    {
        var coordinator = CreateCoordinator();
        var audioPlayer = new AudioPlayerService(new Mock<IColorConsole>().Object);
        var jobRunner = new BackgroundJobRunner(msg => { });
        var handler = new AudioResponseHandler(
            new AppConfig { AudioResponseMode = "text-only" },
            new Mock<IColorConsole>().Object,
            jobRunner,
            audioPlayer,
            null, null, null);

        coordinator.SetAudioHandler(handler);

        var ex = Record.Exception(() => coordinator.OnAgentReplyFull("test text"));
        Assert.Null(ex);

        coordinator.Dispose();
        handler.Dispose();
        audioPlayer.Dispose();
    }

    [Fact]
    public void SetAudioHandler_CanSetNull_AfterNonNull()
    {
        var coordinator = CreateCoordinator();
        var audioPlayer = new AudioPlayerService(new Mock<IColorConsole>().Object);
        var jobRunner = new BackgroundJobRunner(msg => { });
        var handler = new AudioResponseHandler(
            new AppConfig { AudioResponseMode = "text-only" },
            new Mock<IColorConsole>().Object,
            jobRunner,
            audioPlayer,
            null, null, null);

        coordinator.SetAudioHandler(handler);
        coordinator.SetAudioHandler(null);

        var ex = Record.Exception(() => coordinator.OnAgentReplyFull("test"));
        Assert.Null(ex);

        coordinator.Dispose();
        audioPlayer.Dispose();
    }

    [Fact]
    public void SetAudioHandler_SameHandler_NoOp()
    {
        var coordinator = CreateCoordinator();
        var audioPlayer = new AudioPlayerService(new Mock<IColorConsole>().Object);
        var jobRunner = new BackgroundJobRunner(msg => { });
        var handler = new AudioResponseHandler(
            new AppConfig { AudioResponseMode = "text-only" },
            new Mock<IColorConsole>().Object,
            jobRunner,
            audioPlayer,
            null, null, null);

        coordinator.SetAudioHandler(handler);
        coordinator.SetAudioHandler(handler);

        var ex = Record.Exception(() => coordinator.OnAgentReplyFull("test"));
        Assert.Null(ex);

        coordinator.Dispose();
        handler.Dispose();
        audioPlayer.Dispose();
    }

    [Fact]
    public void Dispose_DisposesSetAudioHandler()
    {
        var coordinator = CreateCoordinator();
        var audioPlayer = new AudioPlayerService(new Mock<IColorConsole>().Object);
        var jobRunner = new BackgroundJobRunner(msg => { });
        var handler = new AudioResponseHandler(
            new AppConfig { AudioResponseMode = "text-only" },
            new Mock<IColorConsole>().Object,
            jobRunner,
            audioPlayer,
            null, null, null);

        coordinator.SetAudioHandler(handler);
        coordinator.Dispose();

        // Handler should be disposed after coordinator dispose
        var ex = Assert.Throws<ObjectDisposedException>(() => handler.HandleAudioMarkerAsync("text").Wait());
        Assert.Equal(nameof(AudioResponseHandler), ex.ObjectName);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var coordinator = CreateCoordinator();
        var ex = Record.Exception(() => { coordinator.Dispose(); coordinator.Dispose(); });
        Assert.Null(ex);
    }
}

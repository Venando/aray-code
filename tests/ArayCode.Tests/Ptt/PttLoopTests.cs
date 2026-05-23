using Moq;
using ArayCode;
using ArayCode.Services;
using System.Threading;
using Xunit;

namespace ArayCode.Tests;

public class PttLoopTests
{
    private static IColorConsole CreateMockConsole() => new Mock<IColorConsole>().Object;

    [Fact]
    public async Task RunAsync_QuitViaInput_ReturnsOk()
    {
        var mockState = new Mock<IPttStateMachine>();
        var mockAudio = new Mock<IAudioService>();
        var mockSender = new Mock<ITextMessageSender>();
        var mockInput = new Mock<IInputHandler>();
        var mockPttCtrl = new Mock<IPttController>();
        mockInput.Setup(x => x.HandleInputAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(InputResult.Quit);

        var loop = new AppLoop(
            mockState.Object, mockAudio.Object, mockSender.Object,
            mockInput.Object, mockPttCtrl.Object, CreateMockConsole());

        var cts = new CancellationTokenSource(50);
        var result = await loop.RunAsync(cts.Token);

        Assert.Equal(AppLoopExitCode.Ok, result);
    }

    [Fact]
    public async Task RunAsync_HotkeyPressedStartsRecording()
    {
        var mockState = new Mock<IPttStateMachine>();
        var mockAudio = new Mock<IAudioService>();
        var mockSender = new Mock<ITextMessageSender>();
        var mockInput = new Mock<IInputHandler>();
        var mockPttCtrl = new Mock<IPttController>();

        bool recordingStarted = false;
        mockState.SetupGet(x => x.CurrentState).Returns(PttState.Idle);
        mockState.Setup(x => x.ShouldStartRecording).Returns(() => !recordingStarted);
        mockState.Setup(x => x.ShouldStopRecording).Returns(false);
        mockState.Setup(x => x.OnRecordingStarted()).Callback(() => recordingStarted = true);
        mockPttCtrl.Setup(x => x.PollHotkeyPressed()).Returns(true);
        mockPttCtrl.Setup(x => x.PollHotkeyRelease()).Returns(false);
        mockPttCtrl.Setup(x => x.PollCancelRecording()).Returns(false);
        mockInput.Setup(x => x.HandleInputAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(InputResult.Quit);

        var loop = new AppLoop(
            mockState.Object, mockAudio.Object, mockSender.Object,
            mockInput.Object, mockPttCtrl.Object, CreateMockConsole());

        var cts = new CancellationTokenSource(100);
        await loop.RunAsync(cts.Token);

        Assert.True(recordingStarted);
    }
}

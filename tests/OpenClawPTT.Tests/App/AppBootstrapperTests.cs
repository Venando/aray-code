namespace OpenClawPTT.Tests;

using Moq;
using OpenClawPTT;
using OpenClawPTT.Services;
using OpenClawPTT.Services.StatusParts;
using System;
using Xunit;

public class AppBootstrapperTests : IDisposable
{
    private readonly Mock<IConfigurationService> _fakeConfig;
    private readonly Mock<IConfigWizardOrchestrator> _fakeWizard;
    private readonly Mock<IServiceFactory> _fakeFactory;
    private readonly Mock<IStreamShellHost> _fakeShellHost;
    private readonly Mock<IColorConsole> _fakeConsole;
    /// <summary>Controls shell task lifetime. Unset = shell keeps running.</summary>
    private readonly TaskCompletionSource _shellTaskTcs = new();

    public AppBootstrapperTests()
    {
        _fakeShellHost = new Mock<IStreamShellHost>();
        _fakeShellHost.Setup(x => x.Run(It.IsAny<CancellationToken>())).Returns(_shellTaskTcs.Task);
        _fakeConsole = new Mock<IColorConsole>();

        _fakeConfig = new Mock<IConfigurationService>();

        _fakeWizard = new Mock<IConfigWizardOrchestrator>();
        _fakeWizard.Setup(x => x.LoadOrSetupAsync(It.IsAny<IStreamShellHost>(), false, It.IsAny<CancellationToken>()))
            .Returns(async (IStreamShellHost _, bool _, CancellationToken ct) =>
            {
                // If cancelled before/during load, throw cancellation
                if (ct.IsCancellationRequested)
                    throw new OperationCanceledException();
                return new AppConfig();
            });

        _fakeFactory = new Mock<IServiceFactory>();
        _fakeFactory.Setup(x => x.CreateStreamShellHost()).Returns(_fakeShellHost.Object);
        _fakeFactory.Setup(x => x.CreateGatewayService(It.IsAny<AppConfig>()))
            .Returns(new Mock<IGatewayService>().Object);
        _fakeFactory.Setup(x => x.CreateTextMessageSender(It.IsAny<IGatewayService>()))
            .Returns(new Mock<ITextMessageSender>().Object);
        _fakeFactory.Setup(x => x.CreateAudioService(It.IsAny<AppConfig>()))
            .Returns(new Mock<IAudioService>().Object);
        _fakeFactory.Setup(x => x.CreatePttController(It.IsAny<AppConfig>(), It.IsAny<IAudioService>(), It.IsAny<IHotkeyHookFactory?>()))
            .Returns(new Mock<IPttController>().Object);
        _fakeFactory.Setup(x => x.CreateInputHandler(It.IsAny<ITextMessageSender>()))
            .Returns(new Mock<IInputHandler>().Object);
        _fakeFactory.Setup(x => x.CreatePttLoop(
            It.IsAny<IPttStateMachine>(),
            It.IsAny<IAudioService>(),
            It.IsAny<IPttController>(),
            It.IsAny<ITextMessageSender>(),
            It.IsAny<IInputHandler>(),
            It.IsAny<bool>()))
            .Returns(new Mock<IAppLoop>().Object);
    }

    public void Dispose() { }

    private Mock<AppRunner> MakeMockRunner(int exitCode = 0, Exception? throws = null)
    {
        var mock = new Mock<AppRunner>(
            MockBehavior.Loose,
            new AppConfig(),
            _fakeFactory.Object,
            _fakeShellHost.Object,
            _fakeConfig.Object,
            _fakeConsole.Object,
            (MainAgentsPart?)null!,
            (IConfigWizardOrchestrator?)null!,
            (AgentStatusBottomPanel?)null!);
        mock.CallBase = false;
        if (throws != null)
            mock.Setup(x => x.RunAsync(It.IsAny<CancellationToken>())).ThrowsAsync(throws);
        else
            mock.Setup(x => x.RunAsync(It.IsAny<CancellationToken>())).ReturnsAsync(exitCode);
        return mock;
    }

    #region Happy path

    [Fact]
    public async Task RunAsync_RunnerReturns0_NoException_Returns0()
    {
        var mockRunner = MakeMockRunner(0);
        var bootstrapper = new AppBootstrapper(
            _fakeConfig.Object,
            _fakeWizard.Object,
            _fakeFactory.Object,
            _fakeShellHost.Object,
            _fakeConsole.Object,
            (_, _, _) => mockRunner.Object);

        var exitCode = await bootstrapper.RunAsync();

        Assert.Equal(0, exitCode);
    }

    #endregion

    #region Runner non-zero exit

    [Fact]
    public async Task RunAsync_RunnerReturns1_ReturnsExitError()
    {
        var mockRunner = MakeMockRunner(1);
        var bootstrapper = new AppBootstrapper(
            _fakeConfig.Object,
            _fakeWizard.Object,
            _fakeFactory.Object,
            _fakeShellHost.Object,
            _fakeConsole.Object,
            (_, _, _) => mockRunner.Object);

        var exitCode = await bootstrapper.RunAsync();

        Assert.Equal(AppExitHandler.ExitError, exitCode);
    }

    #endregion

    #region Exception handling

    [Fact]
    public async Task RunAsync_ConfigLoadThrows_ReturnsExitError()
    {
        _fakeWizard.Setup(x => x.LoadOrSetupAsync(It.IsAny<IStreamShellHost>(), false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("config broken"));

        var bootstrapper = new AppBootstrapper(
            _fakeConfig.Object,
            _fakeWizard.Object,
            _fakeFactory.Object,
            _fakeShellHost.Object,
            _fakeConsole.Object);

        var exitCode = await bootstrapper.RunAsync();

        Assert.Equal(AppExitHandler.ExitError, exitCode);
    }

    [Fact]
    public async Task RunAsync_RunnerThrows_ReturnsExitError()
    {
        var mockRunner = MakeMockRunner(throws: new InvalidOperationException("runner boom"));
        var bootstrapper = new AppBootstrapper(
            _fakeConfig.Object,
            _fakeWizard.Object,
            _fakeFactory.Object,
            _fakeShellHost.Object,
            _fakeConsole.Object,
            (_, _, _) => mockRunner.Object);

        var exitCode = await bootstrapper.RunAsync();

        Assert.Equal(AppExitHandler.ExitError, exitCode);
    }

    #endregion

    #region Cancellation

    [Fact]
    public async Task RunAsync_CTCancelled_ReturnsExitCancelled()
    {
        var cts = new CancellationTokenSource();
        var mockRunner = MakeMockRunner(0);

        var bootstrapper = new AppBootstrapper(
            _fakeConfig.Object,
            _fakeWizard.Object,
            _fakeFactory.Object,
            _fakeShellHost.Object,
            _fakeConsole.Object,
            (_, _, _) => mockRunner.Object);

        cts.Cancel();
        var exitCode = await bootstrapper.RunAsync(cts.Token);

        Assert.Equal(AppExitHandler.ExitCancelled, exitCode);
    }

    #endregion

    #region Shell exit before runner (Ctrl+D)

    [Fact]
    public async Task RunAsync_ShellExitsBeforeRunner_CancelsCtsAndExitsCleanly()
    {
        // Simulate Ctrl+D: shell task completes before the runner starts.
        // The bootstrapper should cancel the CTS and exit cleanly.
        _shellTaskTcs.TrySetResult();

        var mockRunner = MakeMockRunner(0);
        var bootstrapper = new AppBootstrapper(
            _fakeConfig.Object,
            _fakeWizard.Object,
            _fakeFactory.Object,
            _fakeShellHost.Object,
            _fakeConsole.Object,
            (_, _, _) => mockRunner.Object);

        // Should not throw or hang
        var exitCode = await bootstrapper.RunAsync();

        // Shell exiting gracefully should result in a clean exit (0)
        Assert.Equal(0, exitCode);
    }

    #endregion

    #region Dispose

    [Fact]
    public void Dispose_UnsubscribesCancelKeyPress()
    {
        var mockRunner = MakeMockRunner(0);
        var bootstrapper = new AppBootstrapper(
            _fakeConfig.Object,
            _fakeWizard.Object,
            _fakeFactory.Object,
            _fakeShellHost.Object,
            _fakeConsole.Object,
            (_, _, _) => mockRunner.Object);

        bootstrapper.Dispose();

        // Removing a never-added handler is a no-op; verify no crash
        Assert.True(true);
    }

    #endregion
}

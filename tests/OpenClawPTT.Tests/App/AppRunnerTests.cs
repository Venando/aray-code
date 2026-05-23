namespace OpenClawPTT.Tests;

using Moq;
using OpenClawPTT;
using OpenClawPTT.Services;
using OpenClawPTT.TTS;
using System;
using System.Threading;
using Xunit;

public class AppRunnerTests
{
    private static AppConfig DefaultConfig => new()
    {
        HotkeyCombination = "Alt+=",
        HoldToTalk = false
    };

    private static IColorConsole CreateMockConsole() => Mock.Of<IColorConsole>();

    private sealed class TestServiceFactory : IServiceFactory
    {
        public IAgentActivityStore? AgentActivityStore => null;
        public IRecentMessageTracker? RecentMessageTracker => null;

        public Mock<IGatewayService> Gateway { get; } = new();
        public Mock<IAudioService> Audio { get; } = new();
        public Mock<IPttController> PttController { get; } = new();
        public Mock<ITextMessageSender> TextSender { get; } = new();
        public Mock<IInputHandler> InputHandler { get; } = new();
        public Mock<IAppLoop> PttLoop { get; } = new();

        public IGatewayService CreateGatewayService(AppConfig cfg, ITtsSummarizer? summarizer = null,
            IPttStateMachine? pttStateMachine = null, Task<ITextToSpeech?>? ttsProviderTask = null) => Gateway.Object;
        public ITtsService CreateTtsService(AppConfig cfg, IColorConsole console) => Mock.Of<ITtsService>();
        public IAudioService CreateAudioService(AppConfig cfg) => Audio.Object;
        public IPttController CreatePttController(AppConfig cfg, IAudioService audioService, IHotkeyHookFactory? hotkeyHookFactory = null) => PttController.Object;
        public ITextMessageSender CreateTextMessageSender(IGatewayService gateway) => TextSender.Object;
        public IInputHandler CreateInputHandler(ITextMessageSender textSender) => InputHandler.Object;
        public IDirectLlmService CreateDirectLlmService(AppConfig cfg) => Mock.Of<IDirectLlmService>();
        public IStreamShellHost CreateStreamShellHost() => Mock.Of<IStreamShellHost>();
        public void InitializeAgentSettingsPersistence(AgentSettingsService agentSettingsService) { }
        public IAgentSettingsPersistence GetAgentSettingsPersistence() => CreatePersistenceMock();
        public IColorConsole CreateColorConsole() => Mock.Of<IColorConsole>();
        public IAppLoop CreatePttLoop(
            IPttStateMachine stateMachine,
            IAudioService audioService,
            IPttController pttController,
            ITextMessageSender textSender,
            IInputHandler inputHandler,
            bool requireConfirmBeforeSend = false,
            bool appendToInput = false,
            StreamShell.IInputHandler? shellInputHandler = null,
            IStreamShellHost? shellHost = null) => PttLoop.Object;

        public ITtsSummarizer CreateTtsSummarizer(IDirectLlmService? directLlm) => Mock.Of<ITtsSummarizer>();
        public IConversationNamingService CreateConversationNamingService(IDirectLlmService? directLlm, AppConfig cfg) => Mock.Of<IConversationNamingService>();
    }

    private static IAgentSettingsPersistence CreatePersistenceMock()
    {
        var mock = new Mock<IAgentSettingsPersistence>();
        mock.Setup(x => x.AllAgentsWithHotkeys).Returns(new List<(AgentInfo Agent, string? Hotkey)>().AsReadOnly());
        mock.Setup(x => x.AllAgentSettings).Returns(new List<(AgentInfo Agent, string? Hotkey, string? Emoji, string? Color, bool ShowInStatusPanel)>().AsReadOnly());
        return mock.Object;
    }

    #region Test 1: AppRunner_Constructs_WithValidDeps

    [Fact]
    public void AppRunner_Constructs_WithValidDeps()
    {
        var factory = new TestServiceFactory();
        var cfg = DefaultConfig;

        var runner = new AppRunner(cfg, factory, Mock.Of<IStreamShellHost>(), Mock.Of<IConfigurationService>(), CreateMockConsole());

        Assert.NotNull(runner);
    }

    #endregion

    #region Test 2: AppRunner_RunAsync_Returns100_OnRestart

    [Fact]
    public async Task AppRunner_RunAsync_Returns100_OnRestart()
    {
        var factory = new TestServiceFactory();

        factory.Gateway.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // First call returns Restart, second call returns Ok — app should loop and return 0
        var callCount = 0;
        factory.PttLoop.Setup(x => x.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1 ? AppLoopExitCode.Restart : AppLoopExitCode.Ok;
            });

        var cfg = DefaultConfig;
        using var runner = new AppRunner(cfg, factory, Mock.Of<IStreamShellHost>(), Mock.Of<IConfigurationService>(), CreateMockConsole());

        var result = await runner.RunAsync(CancellationToken.None);

        // After one restart loop, returns 0 (because second RunAsync returns Ok)
        Assert.Equal(0, result);
        Assert.Equal(2, callCount);
    }

    #endregion

    #region Test 3: AppRunner_Disposes_OwnedResources

    [Fact]
    public async Task AppRunner_Disposes_OwnedResources()
    {
        var factory = new TestServiceFactory();

        factory.Gateway.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        factory.PttLoop.Setup(x => x.RunAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AppLoopExitCode.Ok);

        var cfg = DefaultConfig;
        using var runner = new AppRunner(cfg, factory, Mock.Of<IStreamShellHost>(), Mock.Of<IConfigurationService>(), CreateMockConsole());

        await runner.RunAsync(CancellationToken.None);

        factory.Gateway.Verify(x => x.Dispose(), Times.Once);
        factory.Audio.Verify(x => x.Dispose(), Times.Once);
        factory.PttLoop.Verify(x => x.Dispose(), Times.Once);
    }

    #endregion
}

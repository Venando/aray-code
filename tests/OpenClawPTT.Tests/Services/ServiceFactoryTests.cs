namespace OpenClawPTT.Tests;

using Moq;
using OpenClawPTT;
using OpenClawPTT.Services;
using System;
using System.IO;
using Xunit;

public class ServiceFactoryTests
{
    private static AppConfig DefaultConfig => new()
    {
        HotkeyCombination = "Alt+=",
        HoldToTalk = false
    };

    private static AppConfig DefaultConfigWithAudio => new()
    {
        HotkeyCombination = "Alt+=",
        HoldToTalk = false,
        GroqApiKey = "test-key",
    };

    private static ServiceFactory CreateFactory()
    {
        var configService = new ConfigurationService();
        return new ServiceFactory(configService, new StreamShellHost());
    }

    private static void InitPersistence(ServiceFactory factory, Mock<IColorConsole> console)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ServiceFactoryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var settingsService = new AgentSettingsService(tempDir, console.Object);
        factory.InitializeAgentSettingsPersistence(settingsService);
    }

    /// <summary>
    /// Minimal test-double for IGlobalHotkeyHook that does nothing and never touches /dev/input.
    /// </summary>
    private sealed class NoOpHotkeyHook : IGlobalHotkeyHook
    {
        public event Action? HotkeyPressed { add { } remove { } }
        public event Action? HotkeyReleased { add { } remove { } }
        public event Action<int>? HotkeyIndexPressed { add { } remove { } }
        public event Action<int>? HotkeyIndexReleased { add { } remove { } }
        public event Action? EscapePressed { add { } remove { } }
        public bool BlockEscape { get; set; }
        public void SetHotkey(Hotkey mapping) { }
        public void SetHotkeys(System.Collections.Generic.IEnumerable<Hotkey> hotkeys) { }
        public void Start() { }
        public void Dispose() { }
    }

    /// <summary>
    /// Test-double IHotkeyHookFactory that returns a NoOpHotkeyHook,
    /// preventing real /dev/input access in tests.
    /// </summary>
    private sealed class NoOpHotkeyHookFactory : IHotkeyHookFactory
    {
        public IGlobalHotkeyHook Create(Hotkey mapping, IColorConsole console) => new NoOpHotkeyHook();
    }

    [Fact]
    public void CreatePttController_Returns_IPttController()
    {
        var factory = CreateFactory();
        var cfg = DefaultConfigWithAudio;
        var mockAudio = new Mock<IAudioService>();
        var noOpFactory = new NoOpHotkeyHookFactory();

        var controller = factory.CreatePttController(cfg, mockAudio.Object, noOpFactory);

        Assert.NotNull(controller);
        Assert.IsAssignableFrom<IPttController>(controller);

        controller.Dispose();
    }

    [Fact]
    public void CreatePttLoop_Returns_IPttLoop()
    {
        var factory = CreateFactory();

        var mockStateMachine = new Mock<IPttStateMachine>();
        var mockAudio = new Mock<IAudioService>();
        var mockPttController = new Mock<IPttController>();
        var mockTextSender = new Mock<ITextMessageSender>();
        var mockInputHandler = new Mock<IInputHandler>();

        var loop = factory.CreatePttLoop(
            mockStateMachine.Object,
            mockAudio.Object,
            mockPttController.Object,
            mockTextSender.Object,
            mockInputHandler.Object);

        Assert.NotNull(loop);
        Assert.IsAssignableFrom<IAppLoop>(loop);

        loop.Dispose();
    }

    [Fact]
    public void CreatePttController_ThenDispose_NoDoubleDisposeCrash()
    {
        var factory = CreateFactory();
        var cfg = DefaultConfigWithAudio;
        var mockAudio = new Mock<IAudioService>();
        var noOpFactory = new NoOpHotkeyHookFactory();

        var controller = factory.CreatePttController(cfg, mockAudio.Object, noOpFactory);

        controller.Dispose();

        var ex = Record.Exception(() => controller.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void CreateAudioService_Returns_IAudioService()
    {
        var factory = CreateFactory();
        var cfg = DefaultConfigWithAudio;
        var console = new Mock<IColorConsole>();
        InitPersistence(factory, console);

        var audio = factory.CreateAudioService(cfg);

        Assert.NotNull(audio);
        Assert.IsType<AudioService>(audio);

        audio.Dispose();
    }

    [Fact]
    public void CreateGatewayService_Returns_IGatewayService()
    {
        var factory = CreateFactory();
        var cfg = DefaultConfig;

        var gateway = factory.CreateGatewayService(cfg);

        Assert.NotNull(gateway);
        Assert.IsType<GatewayService>(gateway);

        gateway.Dispose();
    }

    [Fact]
    public void ServiceFactory_WithDefaultConfig_DoesNotThrow()
    {
        var cfg = new AppConfig { GroqApiKey = "test-key" };

        var ex = Record.Exception(() =>
        {
            var factory = CreateFactory();
            var console = new Mock<IColorConsole>();
            InitPersistence(factory, console);
            using var gateway = factory.CreateGatewayService(cfg);
            using var audio = factory.CreateAudioService(cfg);
        });

        Assert.Null(ex);
    }
}

using OpenClawPTT.Services;
using OpenClawPTT.TTS;

namespace OpenClawPTT;

/// <summary>
/// Default implementation of IServiceFactory.
/// </summary>
public class ServiceFactory : IServiceFactory
{
    private readonly IConfigurationService _configService;
    private readonly IStreamShellHost _shellHost;
    private readonly IColorConsole _colorConsole;
    private readonly IAgentStatusTracker? _agentStatusTracker;
    private AgentSettingsService? _agentSettingsService;
    private IAgentSettingsPersistence? _agentSettingsPersistence;

    public ServiceFactory(IConfigurationService configService, IStreamShellHost shellHost, IAgentStatusTracker? agentStatusTracker = null)
    {
        _configService = configService;
        _shellHost = shellHost;
        _colorConsole = new ColorConsole(shellHost);
        _agentStatusTracker = agentStatusTracker;
    }

    public IColorConsole ColorConsole => _colorConsole;

    public IAgentStatusTracker? AgentStatusTracker => _agentStatusTracker;

    /// <summary>
    /// Initialize the agent settings persistence with the settings service.
    /// Called by AppBootstrapper after loading the config.
    /// </summary>
    public void InitializeAgentSettingsPersistence(AgentSettingsService agentSettingsService)
    {
        _agentSettingsService = agentSettingsService;
        _agentSettingsPersistence = new AgentSettingsPersistence(agentSettingsService);
        // Initialize the static bridge for legacy callers (ColorConsole, AgentRegistry)
        AgentSettingsPersistenceLegacy.Initialize(_agentSettingsPersistence);
    }

    public IAgentSettingsPersistence GetAgentSettingsPersistence()
    {
        return _agentSettingsPersistence ?? throw new InvalidOperationException(
            "AgentSettingsPersistence not initialized. Call InitializeAgentSettingsPersistence first.");
    }

    public virtual IGatewayService CreateGatewayService(AppConfig cfg, ITtsSummarizer? summarizer = null,
        IPttStateMachine? pttStateMachine = null, Task<ITextToSpeech?>? ttsProviderTask = null)
    {
        var jobRunner = new BackgroundJobRunner(msg => _colorConsole.Log("jobrunner", msg));
        var replyCoordinator = new ReplyStreamCoordinator(cfg, _colorConsole);
        var toolHandler = new ToolDisplayHandler(cfg.ReservedRightMargin, _shellHost);
        var thinkingHandler = new ThinkingDisplayHandler(cfg, _shellHost);

        // Audio handler is created asynchronously via GatewayService when the TTS
        // provider task completes (parallel init). No synchronous audio handler is
        // passed at construction time — GatewayService wires it on completion.
        AudioResponseHandler? audioHandler = null;

        var coordinator = new AgentOutputCoordinator(
            replyCoordinator, toolHandler, thinkingHandler, audioHandler);

        // Create dependencies for DI injection into GatewayService
        var audioPlayer = new AudioPlayerService(_colorConsole);
        var device = new DeviceIdentity(cfg.DataDir);
        device.EnsureKeypair();
        var gatewayClient = new GatewayClient(cfg, device, new GatewayEventSource(), _colorConsole,
            agentStatusTracker: _agentStatusTracker);

        return new GatewayService(cfg, _colorConsole, coordinator, summarizer, pttStateMachine,
            agentStatusTracker: _agentStatusTracker, ttsProviderTask: ttsProviderTask,
            audioPlayer: audioPlayer, initialGatewayClient: gatewayClient);
    }

    public virtual IAudioService CreateAudioService(AppConfig cfg)
    {
        if (_agentSettingsPersistence == null)
            throw new InvalidOperationException("AgentSettingsPersistence not initialized. Call InitializeAgentSettingsPersistence first.");
        var recorder = new AudioRecorder(cfg.SampleRate, cfg.Channels, cfg.BitsPerSample, cfg.MaxRecordSeconds);
        return new AudioService(cfg, _colorConsole, _agentSettingsPersistence, recorder);
    }

    public IPttController CreatePttController(AppConfig cfg, IAudioService audioService, IHotkeyHookFactory? hotkeyHookFactory = null)
    {
        var controller = new PttController(hotkeyHookFactory ?? new HotkeyHookFactory(), _colorConsole);
        controller.SetHotkey(cfg.HotkeyCombination, cfg.HoldToTalk);
        return controller;
    }

    public IInputHandler CreateInputHandler(ITextMessageSender textSender)
        => new InputHandler(textSender);

    public ITextMessageSender CreateTextMessageSender(IGatewayService gateway)
        => new TextMessageSender(gateway, _colorConsole);

    public virtual IDirectLlmService CreateDirectLlmService(AppConfig cfg)
        => new DirectLlmService(cfg);

    public IStreamShellHost CreateStreamShellHost() => _shellHost;

    public IColorConsole CreateColorConsole() => _colorConsole;

    public IAppLoop CreatePttLoop(
        IPttStateMachine stateMachine,
        IAudioService audioService,
        IPttController pttController,
        ITextMessageSender textSender,
        IInputHandler inputHandler,
        bool requireConfirmBeforeSend = false)
    {
        return new AppLoop(stateMachine, audioService, textSender, inputHandler, pttController, _colorConsole,
            requireConfirmBeforeSend);
    }

    public ITtsService CreateTtsService(AppConfig cfg, IColorConsole console)
    {
        return new TtsService(cfg, console);
    }

    public ITtsSummarizer CreateTtsSummarizer(IDirectLlmService? directLlm)
    {
        return new TtsSummarizer(directLlm, _colorConsole);
    }

    public IConversationNamingService CreateConversationNamingService(IDirectLlmService? directLlm, AppConfig cfg)
    {
        return new ConversationNamingService(directLlm, cfg, _colorConsole);
    }
}

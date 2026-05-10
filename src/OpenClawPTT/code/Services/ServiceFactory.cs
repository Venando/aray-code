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
    private AgentSettingsService? _agentSettingsService;
    private IAgentSettingsPersistence? _agentSettingsPersistence;

    public ServiceFactory(IConfigurationService configService, IStreamShellHost shellHost)
    {
        _configService = configService;
        _shellHost = shellHost;
        _colorConsole = new ColorConsole(shellHost);
    }

    public IColorConsole ColorConsole => _colorConsole;

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
        IPttStateMachine? pttStateMachine = null, ITextToSpeech? ttsProvider = null)
    {
        var jobRunner = new BackgroundJobRunner(msg => _colorConsole.Log("jobrunner", msg));
        var replyCoordinator = new ReplyStreamCoordinator(cfg, _colorConsole);
        var toolHandler = new ToolDisplayHandler(cfg.RightMarginIndent, _shellHost);
        var thinkingHandler = new ThinkingDisplayHandler(cfg, _shellHost);

        AudioResponseHandler? audioHandler = null;
        ITextToSpeech? ttsProviderLocal = ttsProvider;
        if (cfg.AudioResponseMode?.ToLowerInvariant() != "text-only")
        {
            if (ttsProviderLocal == null)
            {
                var ttsService = new TtsService(cfg, _colorConsole);
                ttsProviderLocal = ttsService.Provider;
            }

            if (ttsProviderLocal != null)
            {
                var audioPlayer = new AudioPlayerService(_colorConsole);
                audioHandler = new AudioResponseHandler(
                    cfg, _colorConsole, jobRunner, audioPlayer,
                    summarizer, pttStateMachine, ttsProviderLocal);
            }
        }

        var coordinator = new AgentOutputCoordinator(
            replyCoordinator, toolHandler, thinkingHandler, audioHandler);

        return new GatewayService(cfg, _colorConsole, coordinator, summarizer, pttStateMachine);
    }

    public virtual IAudioService CreateAudioService(AppConfig cfg)
    {
        if (_agentSettingsPersistence == null)
            throw new InvalidOperationException("AgentSettingsPersistence not initialized. Call InitializeAgentSettingsPersistence first.");
        return new AudioService(cfg, _colorConsole, _agentSettingsPersistence);
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

    public ITtsSummarizer CreateTtsSummarizer(IDirectLlmService? directLlm)
    {
        return new TtsSummarizer(directLlm, _colorConsole);
    }
}

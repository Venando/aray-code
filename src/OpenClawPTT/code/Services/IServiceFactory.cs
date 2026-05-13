using OpenClawPTT.Services;
using OpenClawPTT.TTS;

namespace OpenClawPTT;

/// <summary>
/// Creates and wires all application services. Acts as a manual DI container.
/// </summary>
public interface IServiceFactory
{
    /// <summary>
    /// Initialize the agent settings persistence with the settings service.
    /// Called by AppBootstrapper after loading the config.
    /// </summary>
    void InitializeAgentSettingsPersistence(AgentSettingsService agentSettingsService);

    /// <summary>
    /// Get the agent settings persistence instance.
    /// Throws if InitializeAgentSettingsPersistence has not been called yet.
    /// </summary>
    IAgentSettingsPersistence GetAgentSettingsPersistence();

    /// <summary>Optional agent status tracker, if provided by the factory.</summary>
    IAgentStatusTracker? AgentStatusTracker { get; }

    IGatewayService CreateGatewayService(AppConfig cfg, ITtsSummarizer? summarizer = null,
        IPttStateMachine? pttStateMachine = null, Task<ITextToSpeech?>? ttsProviderTask = null);
    IAudioService CreateAudioService(AppConfig cfg);
    IPttController CreatePttController(AppConfig cfg, IAudioService audioService, IHotkeyHookFactory? hotkeyHookFactory = null);
    IInputHandler CreateInputHandler(ITextMessageSender textSender);
    ITextMessageSender CreateTextMessageSender(IGatewayService gateway);
    IDirectLlmService CreateDirectLlmService(AppConfig cfg);
    IStreamShellHost CreateStreamShellHost();
    IColorConsole CreateColorConsole();
    IAppLoop CreatePttLoop(
        IPttStateMachine stateMachine,
        IAudioService audioService,
        IPttController pttController,
        ITextMessageSender textSender,
        IInputHandler inputHandler,
        bool requireConfirmBeforeSend = false);

    ITtsService CreateTtsService(AppConfig cfg, IColorConsole console);

    ITtsSummarizer CreateTtsSummarizer(IDirectLlmService? directLlm);

    IConversationNamingService CreateConversationNamingService(IDirectLlmService? directLlm, AppConfig cfg);
}

using OpenClawPTT.TTS;

namespace OpenClawPTT.Services.TestMode;

/// <summary>
/// Service factory that creates mock services for test mode.
/// Extends the standard ServiceFactory but overrides services that require external connections.
/// </summary>
public sealed class TestModeServiceFactory : ServiceFactory
{
    private readonly string _scenario;
    private readonly IColorConsole _colorConsole;

    /// <summary>
    /// Initializes a new instance of the TestModeServiceFactory.
    /// </summary>
    /// <param name="configService">The configuration service.</param>
    /// <param name="shellHost">The stream shell host.</param>
    /// <param name="scenario">The test scenario to use.</param>
    public TestModeServiceFactory(IConfigurationService configService, IStreamShellHost shellHost, string scenario, IAgentStatusTracker? agentStatusTracker = null)
        : base(configService, shellHost, agentStatusTracker)
    {
        _scenario = scenario;
        _colorConsole = new ColorConsole(shellHost);
    }

    /// <summary>
    /// Creates a mock gateway service that simulates connections without real network activity.
    /// </summary>
    public override IGatewayService CreateGatewayService(AppConfig cfg, ITtsSummarizer? summarizer = null,
        IPttStateMachine? pttStateMachine = null, Task<ITextToSpeech?>? ttsProviderTask = null)
    {
        return new MockGatewayService(_scenario, _colorConsole);
    }

    /// <summary>
    /// Creates a mock audio service that simulates recording without accessing microphone hardware.
    /// </summary>
    public override IAudioService CreateAudioService(AppConfig cfg)
    {
        return new MockAudioService(_scenario, _colorConsole);
    }

    /// <summary>
    /// Creates a mock direct LLM service that returns predefined responses without API calls.
    /// </summary>
    public override IDirectLlmService CreateDirectLlmService(AppConfig cfg)
    {
        return new MockDirectLlmService(_scenario, _colorConsole);
    }

    // Note: All other services (PttController, InputHandler, etc.) use the base implementation
    // as they don't require external connections.
}

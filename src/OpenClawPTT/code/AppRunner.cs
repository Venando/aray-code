namespace OpenClawPTT;

using System.Net.WebSockets;
using OpenClawPTT.Services;
using OpenClawPTT.Services.Commands;
using OpenClawPTT.Services.Diagnostics;
using OpenClawPTT.Services.StatusParts;
using OpenClawPTT.TTS;
using StreamShell;

/// <summary>
/// Owns the top-level application composition and run loop.
/// Disposable so it can be unit-tested in isolation from Program.
/// </summary>
public class AppRunner : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly IServiceFactory _factory;
    private readonly IStreamShellHost _shellHost;
    private readonly IConfigurationService _configService;
    private readonly IColorConsole _console;
    private readonly ErrorLogStore _errorLog;
    private readonly IStatusService _statusService;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Maximum number of consecutive <see cref="AppLoopExitCode.Restart"/> responses
    /// allowed before the run loop gives up and returns an error.
    /// </summary>
    public const int MaxRestartCount = 3;

    public AppRunner(AppConfig cfg, IServiceFactory factory, IStreamShellHost shellHost, IConfigurationService configService, IColorConsole console, MainAgentsPart? mainAgentsPart = null)
    {
        _cfg = cfg;
        _factory = factory;
        _shellHost = shellHost;
        _configService = configService;
        _console = console;
        _errorLog = new ErrorLogStore(cfg.DataDir);
        _statusService = new StatusService(shellHost, mainAgentsPart: mainAgentsPart);

        // Wire agent status tracker if the factory provides one
        if (_factory.AgentStatusTracker != null)
            _statusService.SetAgentStatusTracker(_factory.AgentStatusTracker);

        // Set MainAgentsPart if it wasn't passed to the StatusService constructor
        // (e.g. when using the default runnerFactory path)
        if (mainAgentsPart != null && _statusService is StatusService svc && svc.MainAgentsPart == null)
            svc.SetMainAgentsPart(mainAgentsPart);

        // Initialize status part positions from config
        _statusService.ApplyConfigPositions(_cfg);
    }

    /// <summary>
    /// Runs the app. Returns exit code (0=ok, 1=error, 100=restart).
    /// </summary>
    public virtual async Task<int> RunAsync(CancellationToken ct)
    {
        int result;
        int restartCount = 0;

        _cts?.Dispose();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        do
        {
            result = await RunAppLoopAsync(_cts.Token);
            if (result == (int)AppLoopExitCode.Restart)
            {
                restartCount++;
                if (restartCount >= MaxRestartCount)
                    return (int)AppLoopExitCode.Error;
            }
        } while (result == (int)AppLoopExitCode.Restart);
        return result;
    }

    private async Task<int> RunAppLoopAsync(CancellationToken ct)
    {
        // Apply configured debug level to console
        _console.LogLevel = _cfg.DebugLevel;

        // Create shared state machine and summarizer early so they can be wired into GatewayService
        var pttStateMachine = new PttStateMachine();
        using var directLlmService = _factory.CreateDirectLlmService(_cfg);
        using var ttsSummarizer = _factory.CreateTtsSummarizer(directLlmService.IsConfigured ? directLlmService : null);

        // ── Parallel init: TTS and Direct LLM probing on background threads ──────────
        using var ttsInitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var ttsInitTask = Task.Run(() => InitializeTtsProviderAsync(_cfg, ttsInitCts.Token), ttsInitCts.Token);

        // LLM probe service manages startup probing + re-probes on /appconfig changes
        using var llmProbeService = new DirectLlmProbeService(_configService, _statusService, _console, _factory);
        var llmProbeTask = Task.Run(() => llmProbeService.ProbeOnStartupAsync(directLlmService, _cfg, ct), ct);

        // Create gateway service (fast — no longer blocks on TTS init).
        using var gateway = _factory.CreateGatewayService(_cfg, ttsSummarizer, pttStateMachine,
            ttsProviderTask: ttsInitTask);

        // Subscribe to gateway connection lifecycle events for the status bar.
        // The Disconnected event fires both on initial connect failure (via ReceiveLoop detection)
        // and on permanent reconnect failure (via GatewayReconnector -> lifecycle relay).
        gateway.Connected += () => _statusService.SetServiceStatus(ServiceKind.Gateway, StatusColor.Green);
        gateway.Disconnected += () => _statusService.SetServiceStatus(ServiceKind.Gateway, StatusColor.Red);
        gateway.Reconnecting += () => _statusService.SetServiceStatus(ServiceKind.Gateway, StatusColor.Yellow);

        // Wire ErrorLogStore into GatewayService so SendTextAsync/SendRpcAsync failures are logged
        if (gateway is GatewayService gw)
            gw.SetErrorLogStore(_errorLog);

        // Gateway connect runs in parallel with TTS/LLM init — the TTS task is handled
        // by GatewayService's internal continuation.
        var connectResult = await TryConnectWithGuidanceAsync(gateway, ct);

        if (connectResult == ConnectResult.GiveUp)
            return (int)AppLoopExitCode.Error;

        bool gatewayConnected = connectResult == ConnectResult.Success;
        return await RunPttLoopAsync(gateway, pttStateMachine, directLlmService, ttsSummarizer, gatewayConnected, ct);
    }

    /// <summary>
    /// Initializes the TTS provider on a background thread.
    /// Updates status via <see cref="_statusService"/> as init progresses.
    /// </summary>
    private async Task<ITextToSpeech?> InitializeTtsProviderAsync(AppConfig cfg, CancellationToken ct)
    {
        try
        {
            _console.Log("tts", "Initializing TTS...");
            using var ttsService = new TtsService(cfg, _console);
            ct.ThrowIfCancellationRequested();

            if (ttsService.Provider != null)
            {
                _statusService.SetServiceStatus(ServiceKind.Tts, StatusColor.Green);
                _console.LogOk("tts", $"TTS connected ({ttsService.ProviderType})");
                return ttsService.ReleaseProvider();
            }

            // Provider is null (Edge with no key, etc.) — warn but don't error
            _statusService.SetServiceStatus(ServiceKind.Tts, StatusColor.Red);
            _console.Log("tts", "TTS provider is null (not configured).");
            return null;
        }
        catch (OperationCanceledException)
        {
            _statusService.SetServiceStatus(ServiceKind.Tts, StatusColor.Red);
            throw;
        }
        catch (Exception ex)
        {
            _statusService.SetServiceStatus(ServiceKind.Tts, StatusColor.Red);
            _console.LogError("tts", $"TTS initialization failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Result of the guided connect attempt.</summary>
    private enum ConnectResult { Success, ContinueWithoutGateway, GiveUp }

    /// <summary>
    /// Attempts to connect to the gateway. On failure, classifies the error
    /// and shows actionable guidance instead of crashing.
    /// </summary>
    private async Task<ConnectResult> TryConnectWithGuidanceAsync(IGatewayService gateway, CancellationToken ct)
    {
        try
        {
            _statusService.SetServiceStatus(ServiceKind.Gateway, StatusColor.Yellow);
            _console.PrintInfo("Connecting to gateway...");
            await gateway.ConnectAsync(ct);
            _console.LogOk("gateway", "Gateway connected.");
            // Status set to Green via gateway.Connected event handler (handles initial + reconnect)
            return ConnectResult.Success;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            var classification = GatewayErrorClassifier.Classify(ex);

            // Always log the error
            _errorLog.Write(classification.ToLogEntry());

            // Show user-friendly message
            _console.PrintWarning($"Gateway connection failed [{classification.Code}]");
            _console.PrintWarning($"  {classification.HumanMessage}");

            if (classification.SuggestedActions.Length > 0)
            {
                _console.PrintInfo("  Suggested actions:");
                foreach (var action in classification.SuggestedActions)
                    _console.PrintInfo($"    \u2192 {action}");
            }


            if (classification.ShouldStopApp)
            {
                _statusService.SetServiceStatus(ServiceKind.Gateway, StatusColor.Red);
                _console.PrintError("Cannot continue without gateway connection.");
                return ConnectResult.GiveUp;
            }

            // App stays alive — StreamShell keeps running, user can /reconnect
            _statusService.SetServiceStatus(ServiceKind.Gateway, StatusColor.Red);
            _console.PrintInfo("StreamShell is still available. Use /reconnect to retry, or /quit to exit.");
            return ConnectResult.ContinueWithoutGateway;
        }
    }

    /// <summary>
    /// Wires up the conversation naming pipeline: text sender wrapper, naming service,
    /// and input handler. Returns the assembled objects for caller disposal.
    /// </summary>
    private (ConversationNamingTextMessageSender NamingTextSender, IConversationNamingService NamingService, Services.IInputHandler InputHandler)
        CreateNamingPipeline(IGatewayService gateway, IDirectLlmService directLlmService)
    {
        var textSender = _factory.CreateTextMessageSender(gateway);

        var namingService = _factory.CreateConversationNamingService(
            directLlmService.IsConfigured ? directLlmService : null, _cfg);
        namingService.ConversationNameChanged += name => _statusService.SetConversationName(name);

        // Wire agent replies to naming service for adaptive conversation naming
        gateway.AgentReplyFull += namingService.OnAgentReplyReceived;

        var namingTextSender = new ConversationNamingTextMessageSender(textSender, namingService);
        var inputHandler = _factory.CreateInputHandler(namingTextSender);

        return (namingTextSender, namingService, inputHandler);
    }

    /// <summary>
    /// Creates and registers all StreamShell commands and the agent hotkey service.
    /// Also wires session snapshot cleanup and command-executed events.
    /// </summary>
    private async Task<(
            AgentHotkeyService HotkeyService,
            StreamShellInputHandler ShellCommands,
            SessionResetSnapshotCleaner SnapshotCleaner,
            IAppLoop PttLoop)>
        CreateShellAndHotkeyServicesAsync(
            IGatewayService gateway,
            IPttStateMachine pttStateMachine,
            ConversationNamingTextMessageSender namingTextSender,
            IConversationNamingService namingService,
            IDirectLlmService directLlmService,
            ITtsSummarizer ttsSummarizer,
            IAudioService audioService,
            Services.IInputHandler inputHandler,
            bool gatewayConnected,
            CancellationToken ct)
    {
        var pttController = new PttController();

        var agentHotkeyService = new AgentHotkeyService(
            pttController, namingTextSender, _shellHost, _cfg,
            _factory.GetAgentSettingsPersistence(),
            gatewayService: gateway,
            pttStateMachine: pttStateMachine,
            console: _console);

        var shellCommands = new StreamShellInputHandler(
            _shellHost,
            namingTextSender,
            gateway,
            _configService,
            _cfg,
            onQuit: () => _cts?.Cancel(),
            console: _console,
            agentSettingsPersistence: _factory.GetAgentSettingsPersistence(),
            pttStateMachine: pttStateMachine,
            directLlmService: directLlmService.IsConfigured ? directLlmService : null,
            ttsSummarizer: ttsSummarizer,
            namingService: namingService,
            errorLogStore: _errorLog,
            statusService: _statusService
        );
        shellCommands.CommandExecuted += namingService.OnCommandExecuted;

        var snapshotCleaner = new SessionResetSnapshotCleaner(_factory.AgentStatusTracker);
        shellCommands.CommandExecuted += snapshotCleaner.Handle;

        await shellCommands.RegisterBaseAsync();

        if (gatewayConnected)
            shellCommands.SetGatewayConnected(true);

        if (directLlmService.IsConfigured)
            shellCommands.SetDirectLlmConfigured(true);

        // Wire agent hotkey history printing to the canonical shared method
        agentHotkeyService.PrintSessionHistoryAsync = shellCommands.PrintSessionHistory;

        _console.PrintHelpMenu(_cfg);

        var pttLoop = _factory.CreatePttLoop(
            pttStateMachine, audioService, pttController, namingTextSender, inputHandler,
            requireConfirmBeforeSend: _cfg.RequireConfirmBeforeSend);

        return (agentHotkeyService, shellCommands, snapshotCleaner, pttLoop);
    }

    /// <summary>
    /// Wraps the audio service lifecycle: creates it, subscribes to config changes
    /// for STT provider/model switching, runs the PTT loop, and cleans up.
    /// </summary>
    private async Task<int> RunPttLoopAsync(IGatewayService gateway, IPttStateMachine pttStateMachine, IDirectLlmService directLlmService, ITtsSummarizer ttsSummarizer, bool gatewayConnected, CancellationToken ct)
    {
        using var audioService = _factory.CreateAudioService(_cfg);
        // AudioService constructor creates a transcriber synchronously — mark STT as ready
        _statusService.SetServiceStatus(ServiceKind.Stt, StatusColor.Green);

        // Re-create transcriber when config changes (e.g. STT provider/model switched via /reconfigure)
        void OnConfigSaved(AppConfig newCfg)
        {
            _statusService.SetServiceStatus(ServiceKind.Stt, StatusColor.Yellow);
            try
            {
                audioService.RecreateTranscriber(newCfg, _console);
                _statusService.SetServiceStatus(ServiceKind.Stt, StatusColor.Green);
            }
            catch (Exception ex)
            {
                _statusService.SetServiceStatus(ServiceKind.Stt, StatusColor.Red);
                _console.PrintError($"Failed to update STT: {ex.Message}");
            }
        }
        _configService.ConfigSaved += OnConfigSaved;

        try
        {
            var (namingTextSender, namingService, inputHandler) =
                CreateNamingPipeline(gateway, directLlmService);

            using var namingDisposable = (IDisposable)namingService;

            var (hotkeyService, shellCommands, snapshotCleaner, pttLoop) =
                await CreateShellAndHotkeyServicesAsync(
                    gateway, pttStateMachine, namingTextSender, namingService,
                    directLlmService, ttsSummarizer, audioService, inputHandler,
                    gatewayConnected, ct);

            using var hotkeyDisposable = hotkeyService;
            using var shellDisposable = shellCommands;
            using var snapshotDisposable = snapshotCleaner;
            using var pttLoopDisposable = pttLoop;

            return (int)(await pttLoop.RunAsync(ct));
        }
        finally
        {
            _configService.ConfigSaved -= OnConfigSaved;
        }
    }

    /// <summary>Access the error log store (used by StreamShell commands).</summary>
    internal ErrorLogStore GetErrorLogStore() => _errorLog;

    public void Dispose()
    {
        _cts?.Dispose();
        _cts = null;
        _errorLog.Dispose();
    }
}
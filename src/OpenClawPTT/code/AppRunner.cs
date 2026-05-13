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
                _statusService.SetTtsStatus("Connected", StatusColor.Green);
                _console.LogOk("tts", $"TTS connected ({ttsService.ProviderType})");
                return ttsService.ReleaseProvider();
            }

            // Provider is null (Edge with no key, etc.) — warn but don't error
            _statusService.SetTtsStatus("Disconnected", StatusColor.Red);
            _console.Log("tts", "TTS provider is null (not configured).");
            return null;
        }
        catch (OperationCanceledException)
        {
            _statusService.SetTtsStatus("Disconnected", StatusColor.Red);
            throw;
        }
        catch (Exception ex)
        {
            _statusService.SetTtsStatus("Disconnected", StatusColor.Red);
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
            _console.PrintInfo("Connecting to gateway...");
            await gateway.ConnectAsync(ct);
            _console.LogOk("gateway", "Gateway connected.");
            _statusService.SetGatewayStatus("Connected", StatusColor.Green);
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

            _statusService.SetGatewayStatus("Connecting", StatusColor.Yellow);

            if (classification.ShouldStopApp)
            {
                _statusService.SetGatewayStatus("Disconnected", StatusColor.Red);
                _console.PrintError("Cannot continue without gateway connection.");
                return ConnectResult.GiveUp;
            }

            // App stays alive — StreamShell keeps running, user can /reconnect
            _statusService.SetGatewayStatus("Disconnected", StatusColor.Red);
            _console.PrintInfo("StreamShell is still available. Use /reconnect to retry, or /quit to exit.");
            return ConnectResult.ContinueWithoutGateway;
        }
    }

    private async Task<int> RunPttLoopAsync(IGatewayService gateway, IPttStateMachine pttStateMachine, IDirectLlmService directLlmService, ITtsSummarizer ttsSummarizer, bool gatewayConnected, CancellationToken ct)
    {
        using var audioService = _factory.CreateAudioService(_cfg);

        // Re-create transcriber when config changes (e.g. STT provider/model switched via /reconfigure)
        void OnConfigSaved(AppConfig newCfg)
        {
            try { audioService.RecreateTranscriber(newCfg, _console); }
            catch (Exception ex) { _console.PrintError($"Failed to update STT: {ex.Message}"); }
        }
        _configService.ConfigSaved += OnConfigSaved;

        try
        {
        var textSender = _factory.CreateTextMessageSender(gateway);

        // Wire up conversation naming: wrap text sender and connect to status bar
        using var namingService = _factory.CreateConversationNamingService(
            directLlmService.IsConfigured ? directLlmService : null, _cfg);
        namingService.ConversationNameChanged += name => _statusService.SetConversationName(name);

        // Wire agent replies to naming service for adaptive conversation naming
        gateway.AgentReplyFull += namingService.OnAgentReplyReceived;

        var namingTextSender = new ConversationNamingTextMessageSender(textSender, namingService);

        var inputHandler = _factory.CreateInputHandler(namingTextSender);

        // Agent settings (loaded in AppBootstrapper, already merged into AgentRegistry)
        var pttController = new PttController();

        using var agentHotkeyService = new AgentHotkeyService(
            pttController, namingTextSender, _shellHost, _cfg,
            _factory.GetAgentSettingsPersistence(),
            gatewayService: gateway,
            pttStateMachine: pttStateMachine,
            console: _console);

        // Register StreamShell commands (/quit, /reconfigure) before PTT loop
        using var shellCommands = new StreamShellInputHandler(
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

        using var snapshotCleaner = new SessionResetSnapshotCleaner(_factory.AgentStatusTracker);
        shellCommands.CommandExecuted += snapshotCleaner.Handle;

        await shellCommands.RegisterBaseAsync();

        // Register gateway-dependent commands based on connection result
        if (gatewayConnected)
            shellCommands.SetGatewayConnected(true);

        // Register /llm command if Direct LLM is configured
        if (directLlmService.IsConfigured)
            shellCommands.SetDirectLlmConfigured(true);

        // Wire agent hotkey history printing to the canonical shared method
        agentHotkeyService.PrintSessionHistoryAsync = shellCommands.PrintSessionHistory;

        _console.PrintHelpMenu(_cfg);

        using IAppLoop pttLoop = _factory.CreatePttLoop(
            pttStateMachine, audioService, pttController, namingTextSender, inputHandler,
            requireConfirmBeforeSend: _cfg.RequireConfirmBeforeSend);

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
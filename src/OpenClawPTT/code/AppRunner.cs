namespace OpenClawPTT;

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
public partial class AppRunner : IDisposable
{
    private AppConfig _cfg;
    private readonly IServiceFactory _factory;
    private readonly IStreamShellHost _shellHost;
    private readonly IConfigurationService _configService;
    private readonly IConfigWizardOrchestrator? _wizard;
    private readonly IColorConsole _console;
    private readonly ErrorLogStore _errorLog;
    private readonly IStatusService _statusService;
    private readonly StreamShell.IBottomPanel? _bottomPanel;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Maximum number of consecutive <see cref="AppLoopExitCode.Restart"/> responses
    /// allowed before the run loop gives up and returns an error.
    /// </summary>
    public const int MaxRestartCount = 3;

    public AppRunner(AppConfig cfg, IServiceFactory factory, IStreamShellHost shellHost, IConfigurationService configService, IColorConsole console, MainAgentsPart? mainAgentsPart = null, IConfigWizardOrchestrator? wizard = null, StreamShell.IBottomPanel? bottomPanel = null)
    {
        _cfg = cfg;
        _factory = factory;
        _shellHost = shellHost;
        _configService = configService;
        _wizard = wizard;
        _console = console;
        _errorLog = new ErrorLogStore(cfg.DataDir);
        _statusService = new StatusService(shellHost, mainAgentsPart: mainAgentsPart);
        _bottomPanel = bottomPanel;

        // Wire agent status tracker if the factory provides one
        if (_factory.AgentActivityStore != null)
            _statusService.SetAgentActivityStore(_factory.AgentActivityStore);

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

        // Wire TTS synthesis runtime status to the status dot:
        // Green on successful synthesis, Red on failure.
        gateway.OnTtsSynthesisStatus = success =>
            _statusService.SetServiceStatus(ServiceKind.Tts, success ? StatusColor.Green : StatusColor.Red);

        // Subscribe to gateway connection lifecycle events for the status bar.
        gateway.Connected += () => _statusService.SetServiceStatus(ServiceKind.Gateway, StatusColor.Green);
        // Disconnected: fires when the WebSocket drops (ReceiveLoop catches it).
        // ReconnectFailed: fires only after all reconnection attempts are exhausted.
        gateway.Disconnected += () => _statusService.SetServiceStatus(ServiceKind.Gateway, StatusColor.Red);
        gateway.ReconnectFailed += () => _statusService.SetServiceStatus(ServiceKind.Gateway, StatusColor.Red);
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
    /// Hard ceiling for the entire gateway connection attempt (including handshake
    /// and authentication). If the gateway is unreachable or the handshake hangs,
    /// we abort after this time rather than staying stuck indefinitely.
    /// </summary>
    private static readonly TimeSpan GlobalConnectTimeout = TimeSpan.FromSeconds(45);

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
            _console.Log("gateway", $"[connect] Starting connection attempt (hard timeout: {GlobalConnectTimeout.TotalSeconds}s)...");

            using var timeoutCts = new CancellationTokenSource(GlobalConnectTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            var connectTask = gateway.ConnectAsync(linked.Token);
            var delayTask = Task.Delay(GlobalConnectTimeout, linked.Token);

            var finished = await Task.WhenAny(connectTask, delayTask).ConfigureAwait(false);

            // Determine if this is a genuine timeout (not user cancellation).
            // Covers both: delayTask won the race, AND connectTask won but faulted
            // because the timeout token fired (OCE misclassification fix).
            bool isTimeout = finished == delayTask
                || (finished == connectTask && !connectTask.IsCompletedSuccessfully
                    && timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested);

            if (isTimeout)
            {
                _console.LogError("gateway", "[connect] Global connect timeout exceeded — aborting stuck connection.");

                // Cancel the linked token to nudge the abandoned connectTask
                try { linked.Cancel(); } catch { /* already cancelled */ }

                // Give the abandoned task a short grace period to unwind and release
                // ReconnectLock before we throw (lock-contention fix).
                try { await connectTask.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); }
                catch { /* best effort — task may have faulted or still be running */ }

                throw new TimeoutException(
                    $"Gateway connection did not complete within {GlobalConnectTimeout.TotalSeconds}s. " +
                    "The connection may be stuck in DNS resolution, TCP handshake, or waiting for the server.");
            }

            await connectTask.ConfigureAwait(false);
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
    /// Wires up the conversation naming pipeline when <see cref="AppConfig.ConversationNamePosition"/>
    /// is not <see cref="DisplayPosition.None"/>. Otherwise returns the raw text sender and null naming service.
    /// </summary>
    private (ITextMessageSender TextSender, IConversationNamingService? NamingService, Services.IInputHandler InputHandler)
        CreateNamingPipeline(IGatewayService gateway, IDirectLlmService directLlmService)
    {
        var textSender = _factory.CreateTextMessageSender(gateway);

        if (_cfg.ConversationNamePosition == DisplayPosition.None)
        {
            return (textSender, null, _factory.CreateInputHandler(textSender));
        }

        var namingService = _factory.CreateConversationNamingService(
            directLlmService.IsConfigured ? directLlmService : null, _cfg);
        namingService.ConversationNameChanged += name => _statusService.SetConversationName(name);

        // Wire agent replies to naming service for adaptive conversation naming
        gateway.AgentReplyFull += namingService.OnAgentReplyReceived;
        gateway.AgentReplyFinal += namingService.OnAgentReplyFinalReceived;

        var namingTextSender = new ConversationNamingTextMessageSender(textSender, namingService);
        return (namingTextSender, namingService, _factory.CreateInputHandler(namingTextSender));
    }

    /// <summary>
    /// Wraps the audio service lifecycle: creates it, subscribes to config changes
    /// for STT provider/model switching, runs the PTT loop, and cleans up.
    /// </summary>
    private async Task<int> RunPttLoopAsync(IGatewayService gateway, IPttStateMachine pttStateMachine, IDirectLlmService directLlmService, ITtsSummarizer ttsSummarizer, bool gatewayConnected, CancellationToken ct)
    {
        using var audioService = _factory.CreateAudioService(_cfg);

        // Wire transcription lifecycle to STT status bar
        audioService.TranscriptionStatusCallback = (phase, _) =>
        {
            switch (phase)
            {
                case TranscriptionPhase.Started:
                    _statusService.SetServiceStatus(ServiceKind.Stt, StatusColor.Yellow);
                    break;
                case TranscriptionPhase.Succeeded:
                    _statusService.SetServiceStatus(ServiceKind.Stt, StatusColor.Green);
                    break;
                case TranscriptionPhase.Failed:
                case TranscriptionPhase.TimedOut:
                    _statusService.SetServiceStatus(ServiceKind.Stt, StatusColor.Red);
                    break;
            }
        };

        // AudioService constructor succeeded — but the transcriber hasn't been
        // verified yet. Set Yellow and verify on a background thread so the
        // animated transitioning state is visible during verification.
        _statusService.SetServiceStatus(ServiceKind.Stt, StatusColor.Yellow);
        _ = Task.Run(async () =>
        {
            try
            {
                await audioService.VerifyTranscriberAsync(_cfg, _console, ct);
                _statusService.SetServiceStatus(ServiceKind.Stt, StatusColor.Green);
            }
            catch (OperationCanceledException)
            {
                // App shutting down — verification cancelled, leave status as-is
            }
            catch (Exception ex)
            {
                _statusService.SetServiceStatus(ServiceKind.Stt, StatusColor.Red);
                _console.LogError("stt", $"STT verification failed: {ex.Message}");
            }
        });

        // Store delegate references for config change handlers
        Action<ConfigChangedEventArgs> onGatewayConfigSaved = e => HandleGatewayConfigChanged(e, gateway);
        Action<ConfigChangedEventArgs> onDisplayConfigSaved = HandleDisplayConfigChanged;
        Action<ConfigChangedEventArgs> onConfigSaved = e => HandleSttConfigChanged(e, audioService);
        Action<ConfigChangedEventArgs> onTtsConfigSaved = e => HandleTtsConfigChanged(e, gateway);

        _configService.ConfigSaved += onGatewayConfigSaved;
        _configService.ConfigSaved += onDisplayConfigSaved;
        _configService.ConfigSaved += onConfigSaved;
        _configService.ConfigSaved += onTtsConfigSaved;

        try
        {
            var (textSender, namingService, inputHandler) =
                CreateNamingPipeline(gateway, directLlmService);

            using var namingDisposable = namingService as IDisposable;

            var (hotkeyService, shellCommands, snapshotCleaner, pttLoop) =
                await CreateShellAndHotkeyServicesAsync(
                    gateway, pttStateMachine, textSender, namingService,
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
            _configService.ConfigSaved -= onGatewayConfigSaved;
            _configService.ConfigSaved -= onDisplayConfigSaved;
            _configService.ConfigSaved -= onConfigSaved;
            _configService.ConfigSaved -= onTtsConfigSaved;
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
namespace OpenClawPTT;

using System.Net.WebSockets;
using OpenClawPTT.Services;
using OpenClawPTT.Services.Diagnostics;
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
    private CancellationTokenSource? _cts;

    /// <summary>
    /// Maximum number of consecutive <see cref="AppLoopExitCode.Restart"/> responses
    /// allowed before the run loop gives up and returns an error.
    /// </summary>
    public const int MaxRestartCount = 3;

    public AppRunner(AppConfig cfg, IServiceFactory factory, IStreamShellHost shellHost, IConfigurationService configService, IColorConsole console)
    {
        _cfg = cfg;
        _factory = factory;
        _shellHost = shellHost;
        _configService = configService;
        _console = console;
        _errorLog = new ErrorLogStore(cfg.DataDir);
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

        using var gateway = _factory.CreateGatewayService(_cfg, ttsSummarizer, pttStateMachine);

        // Wire ErrorLogStore into GatewayService so SendTextAsync/SendRpcAsync failures are logged
        if (gateway is GatewayService gw)
            gw.SetErrorLogStore(_errorLog);

        // Try to connect. On failure, classify the error, show guidance, and
        // continue with StreamShell alive so the user can run /reconnect.
        var connectResult = await TryConnectWithGuidanceAsync(gateway, ct);
        if (connectResult == ConnectResult.GiveUp)
            return (int)AppLoopExitCode.Error;

        return await RunPttLoopAsync(gateway, pttStateMachine, directLlmService, ttsSummarizer, ct);
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
            _errorLog.Write(new ErrorLogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = "error",
                Category = "gateway",
                Code = classification.Code,
                Message = classification.HumanMessage,
                SuggestedActions = classification.SuggestedActions,
                RawException = classification.RawMessage,
                StackTrace = classification.StackTrace
            });

            // Show user-friendly message
            _console.PrintWarning($"Gateway connection failed [{classification.Code}]");
            _console.PrintWarning($"  {classification.HumanMessage}");

            if (classification.SuggestedActions.Length > 0)
            {
                _console.PrintInfo("  Suggested actions:");
                foreach (var action in classification.SuggestedActions)
                    _console.PrintInfo($"    → {action}");
            }

            if (classification.ShouldStopApp)
            {
                _console.PrintError("Cannot continue without gateway connection.");
                return ConnectResult.GiveUp;
            }

            // App stays alive — StreamShell keeps running, user can /reconnect
            _console.PrintInfo("StreamShell is still available. Use /reconnect to retry, or /quit to exit.");
            return ConnectResult.ContinueWithoutGateway;
        }
    }

    private async Task<int> RunPttLoopAsync(IGatewayService gateway, IPttStateMachine pttStateMachine, IDirectLlmService directLlmService, ITtsSummarizer ttsSummarizer, CancellationToken ct)
    {
        using var audioService = _factory.CreateAudioService(_cfg);
        var textSender = _factory.CreateTextMessageSender(gateway);
        var inputHandler = _factory.CreateInputHandler(textSender);

        // Agent settings (loaded in AppBootstrapper, already merged into AgentRegistry)
        var pttController = new PttController();

        using var agentHotkeyService = new AgentHotkeyService(
            pttController, textSender, _shellHost, _cfg,
            _factory.GetAgentSettingsPersistence(),
            gatewayService: gateway,
            pttStateMachine: pttStateMachine,
            console: _console);

        // Register StreamShell commands (/quit, /reconfigure) before PTT loop
        using var shellCommands = new StreamShellInputHandler(
            _shellHost,
            textSender,
            gateway,
            _configService,
            _cfg,
            onQuit: () => _cts?.Cancel(),
            console: _console,
            agentSettingsPersistence: _factory.GetAgentSettingsPersistence(),
            pttStateMachine: pttStateMachine,
            directLlmService: directLlmService.IsConfigured ? directLlmService : null,
            ttsSummarizer: ttsSummarizer,
            errorLogStore: _errorLog
        );
        await shellCommands.RegisterAsync();
        _console.PrintHelpMenu(_cfg);

        using IAppLoop pttLoop = _factory.CreatePttLoop(
            pttStateMachine, audioService, pttController, textSender, inputHandler,
            requireConfirmBeforeSend: _cfg.RequireConfirmBeforeSend);

        return (int)(await pttLoop.RunAsync(ct));
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

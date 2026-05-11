using OpenClawPTT.Services;

namespace OpenClawPTT;

/// <summary>
/// Orchestrates application startup: console configuration, cancellation setup,
/// service resolution, and the run loop.
/// </summary>
public sealed class AppBootstrapper : IDisposable
{
    private readonly IServiceFactory _factory;
    private readonly IConfigurationService _configService;
    private readonly IStreamShellHost _shellHost;
    private readonly Func<AppConfig, IServiceFactory, AppRunner> _runnerFactory;
    private readonly bool _testModeEnabled;
    private CancellationTokenSource? _cts;

    private readonly IColorConsole _console;

    public AppBootstrapper(
        IConfigurationService configService,
        IServiceFactory factory,
        IStreamShellHost shellHost,
        IColorConsole console,
        Func<AppConfig, IServiceFactory, AppRunner>? runnerFactory = null,
        bool testModeEnabled = false)
    {
        _configService = configService;
        _factory = factory;
        _shellHost = shellHost;
        _console = console;
        _testModeEnabled = testModeEnabled;
        _runnerFactory = runnerFactory ?? ((cfg, f) => new AppRunner(cfg, f, _shellHost, _configService, console));
    }

    /// <summary>Runs the application and returns the exit code.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        _console.PrintBanner();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Console.CancelKeyPress += OnCancelKeyPress;

        Exception? ex = null;
        int runnerExitCode = 0;

        try
        {
            // Start StreamShell UI (non-blocking)
            var shellTask = _shellHost.Run(_cts.Token);

            // When StreamShell exits unexpectedly (e.g. Ctrl+D), cancel the app's
            // CTS so the runner loop also stops — killing both, not just StreamShell.
            // When the app shuts down normally (/quit), _cts is already cancelled
            // so this continuation is a no-op.
            _ = shellTask.ContinueWith(_ =>
            {
                if (_cts is { IsCancellationRequested: false })
                {
                    try { _cts.Cancel(); } catch (ObjectDisposedException) { }
                }
            }, TaskContinuationOptions.ExecuteSynchronously);

            var cfg = await _configService.LoadOrSetupAsync(_shellHost, ct: _cts.Token);

            // Apply terminal display configuration from loaded config
            _console.ApplyConsoleConfig(cfg);

            // Load persistent agent settings from agents.json and initialize DI
            var agentSettings = new AgentSettingsService(cfg.DataDir, _console);
            agentSettings.Load();
            _factory.InitializeAgentSettingsPersistence(agentSettings);

            using var runner = _runnerFactory(cfg, _factory);
            runnerExitCode = await runner.RunAsync(_cts.Token);
        }
        catch (Exception caught)
        {
            ex = caught;
        }

        if (runnerExitCode != 0)
            return runnerExitCode;

        if (ex != null)
            return new AppExitHandler(_console).HandleExit(ex);

        return 0;
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        // Ctrl+C: suppress process termination but do NOT cancel the app.
        // The user wants Ctrl+C to not kill the app — it's handled by StreamShell as Copy.
        // Ctrl+Break can also arrive here; we ignore it too.
        e.Cancel = true;
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= OnCancelKeyPress;
        _cts?.Dispose();
    }
}

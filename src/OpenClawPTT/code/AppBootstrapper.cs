using Spectre.Console;
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

            var cfg = await _configService.LoadOrSetupAsync(_shellHost, ct: _cts.Token);

            // Compute final right-edge margin once: max(config indent, 10% of console width)
            cfg.ReservedRightMargin = Math.Max(
                cfg.RightMarginIndent,
                (int)(ConsoleHelper.GetWindowWidth() * 0.1));

            // Apply StreamShell settings and console properties from loaded AppConfig
            _shellHost.SetRightMarginIndent(cfg.ReservedRightMargin);
            _console.UserMessagePrefix = cfg.UserMessagePrefix;
            _console.ReservedRightMargin = cfg.ReservedRightMargin;

            // Match input prefix visual width to user message prefix (keep StreamShell default markup)
            int prefixWidth = Markup.Remove(cfg.UserMessagePrefix).Length;
            _shellHost.SetInputPrefix($"[bold SkyBlue1]{new string(' ', Math.Max(0, prefixWidth - 2))}> [/]");
            _shellHost.SetContinuationPrefix(new string(' ', prefixWidth));

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
        e.Cancel = true;
        _cts?.Cancel();
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= OnCancelKeyPress;
        _cts?.Dispose();
    }
}

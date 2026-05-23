using ArayCode.Services;
using ArayCode.Services.StatusParts;
using ArayCode.Services.TestMode;

namespace ArayCode;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            return await RunAppAsync(args);
        }
        catch (Exception ex)
        {
            // Last-resort fallback: if StreamShell is dead or the exception
            // happened outside the normal try/catch, write directly to the
            // console so the user sees what went wrong before the window closes.
            try
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"\n═══ Fatal error (app will close) ═══");
                Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
                Console.Error.WriteLine($"{ex.StackTrace}");
                if (ex.InnerException != null)
                    Console.Error.WriteLine($"Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                Console.ResetColor();
                Console.Error.WriteLine();
                Console.Write("Press Enter to close...");
                Console.ReadLine();
            }
            catch
            {
                // Even Console.WriteLine can fail (no terminal, redirected, etc.)
                // Nothing more we can do — let the process exit.
            }
            return 1;
        }
    }

    private static async Task<int> RunAppAsync(string[] args)
    {
        // Parse command-line arguments for test mode
        var (testModeEnabled, testScenario) = ParseCommandLineArgs(args);

        var cts = new CancellationTokenSource();
        IConfigurationService configService = new ConfigurationService();
        IConfigWizardOrchestrator wizard = new ConfigWizardOrchestrator(configService);
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var shellHost = new StreamShellHost();
        shellHost.SetExpDecayRate(10);

        // Create agent activity store (replaces tracker + snapshots)
        var activityStore = new AgentActivityStore();
        var mainAgentsPart = new MainAgentsPart(activityStore);

        // Use TestModeServiceFactory when test mode is enabled
        ServiceFactory factory;
        if (testModeEnabled)
        {
            factory = new TestModeServiceFactory(configService, shellHost, testScenario, activityStore);
        }
        else
        {
            factory = new ServiceFactory(configService, shellHost, activityStore);
        }

        var colorConsole = factory.CreateColorConsole();

        // Load config to determine which bottom panel to use
        var appConfig = configService.Load() ?? new AppConfig();

        // Set up the agent status bottom panel
        // Register a factory so the panel can be recreated (e.g. after wizards replace it)
        StreamShell.IBottomPanel appStatusPanel = appConfig.UseAgentStatusPanel
            ? new AgentStatusBottomPanel(activityStore, configService)
            : new AppStatusBottomPanel(mainAgentsPart, appConfig.BottomPanelLineCount);
        shellHost.SetDefaultPanel(appStatusPanel);
        shellHost.SetDefaultPanelFactory(() => appConfig.UseAgentStatusPanel
            ? new AgentStatusBottomPanel(activityStore, configService)
            : new AppStatusBottomPanel(mainAgentsPart, appConfig.BottomPanelLineCount));

        var bootstrapper = new AppBootstrapper(configService, wizard, factory, shellHost, colorConsole,
            mainAgentsPart: mainAgentsPart, testModeEnabled: testModeEnabled,
            bottomPanel: appStatusPanel);
        var exitCode = await bootstrapper.RunAsync(cts.Token);

        bootstrapper.Dispose();
        shellHost.Dispose();
        cts.Dispose();

        return exitCode;
    }

    /// <summary>
    /// Parses command-line arguments for test mode options.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Tuple of (testModeEnabled, testScenario).</returns>
    private static (bool testModeEnabled, string testScenario) ParseCommandLineArgs(string[] args)
    {
        bool testModeEnabled = false;
        string testScenario = TestScenarios.Default;

        foreach (var arg in args)
        {
            var lowerArg = arg.ToLowerInvariant();

            // Check for test mode flags
            if (lowerArg == "--test-mode" || lowerArg == "-t")
            {
                testModeEnabled = true;
            }
            // Check for test scenario
            else if (lowerArg.StartsWith("--test-scenario="))
            {
                testModeEnabled = true;
                testScenario = arg.Substring("--test-scenario=".Length);
            }
            else if (lowerArg.StartsWith("--test-scenario:"))
            {
                testModeEnabled = true;
                testScenario = arg.Substring("--test-scenario:".Length);
            }
        }

        // Validate scenario
        if (!TestScenarios.IsValid(testScenario))
        {
            Console.WriteLine($"[WARNING] Unknown test scenario: '{testScenario}'. Using default '{TestScenarios.Default}'.", ConsoleColor.Yellow);
            testScenario = TestScenarios.Default;
        }

        return (testModeEnabled, testScenario);
    }
}

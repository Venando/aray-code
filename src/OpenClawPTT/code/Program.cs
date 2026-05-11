using OpenClawPTT.Services;
using OpenClawPTT.Services.TestMode;

namespace OpenClawPTT;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Parse command-line arguments for test mode
        var (testModeEnabled, testScenario) = ParseCommandLineArgs(args);

        var cts = new CancellationTokenSource();
        IConfigurationService configService = new ConfigurationService();
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var shellHost = new StreamShellHost();

        // Create agent status tracker and bottom panel
        var agentStatusTracker = new AgentStatusTracker();

        // Use TestModeServiceFactory when test mode is enabled
        ServiceFactory factory;
        if (testModeEnabled)
        {
            factory = new TestModeServiceFactory(configService, shellHost, testScenario, agentStatusTracker);
        }
        else
        {
            factory = new ServiceFactory(configService, shellHost, agentStatusTracker);
        }

        var colorConsole = factory.CreateColorConsole();

        // Load saved config so BottomPanelLineCount is honoured
        var appConfig = configService.Load() ?? new AppConfig();
        var agentStatusPanel = new AgentStatusBottomPanel(agentStatusTracker, appConfig.BottomPanelLineCount);
        shellHost.SetDefaultPanel(agentStatusPanel);

        var bootstrapper = new AppBootstrapper(configService, factory, shellHost, colorConsole, null, testModeEnabled);
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

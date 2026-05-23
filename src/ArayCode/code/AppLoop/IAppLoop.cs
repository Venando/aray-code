namespace ArayCode.Services;

/// <summary>Exit codes returned by the PTT main loop.</summary>
public enum AppLoopExitCode
{
    Ok = 0,
    Error = 1,
    Restart = 100
}

/// <summary>
/// Extracts the main PTT event loop from Program.RunPttLoop into a testable injectable service.
/// </summary>
public interface IAppLoop : IDisposable
{
    /// <summary>The exit code from the last RunAsync call.</summary>
    AppLoopExitCode ExitCode { get; }

    /// <summary>Runs the PTT loop until cancellation or quit/restart.</summary>
    Task<AppLoopExitCode> RunAsync(CancellationToken ct);
}

using ArayCode.Services;

namespace ArayCode;

/// <summary>
/// Maps exceptions to process exit codes and orchestrates the shutdown user experience
/// (error messages, key-press waits).
/// </summary>
public sealed class AppExitHandler : IDisposable
{
    /// <summary>Returned when the application exits normally after cancellation.</summary>
    public const int ExitCancelled = 0;

    /// <summary>Returned when the application exits due to a handled error.</summary>
    public const int ExitError = 1;

    private readonly IColorConsole _console;

    public AppExitHandler(IColorConsole console)
    {
        _console = console;
    }

    /// <summary>
    /// Handles <paramref name="ex"/> and returns the appropriate process exit code.
    /// </summary>
    public int HandleExit(Exception? ex)
    {
        switch (ex)
        {
            case OperationCanceledException:
                return ExitCancelled;

            case GatewayException gex:
                _console.PrintGatewayError(gex.Message, gex.DetailCode, gex.RecommendedStep);
                TryReadKey();
                return ExitError;

            case Exception ex2:
                _console.PrintError($"Fatal: {ex2.Message}. Press any button");
#if DEBUG
                Console.Error.WriteLine(ex2.StackTrace);
#endif
                TryReadKey();
                return ExitError;

            default:
                return ExitCancelled;
        }
    }

    private static void TryReadKey()
    {
        try
        {
            if (Console.KeyAvailable)
                Console.ReadKey(intercept: true);
        }
        catch (InvalidOperationException) { /* no console or stdin redirected */ }
    }

    public void Dispose() { }
}

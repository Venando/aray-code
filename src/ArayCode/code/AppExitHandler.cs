using ArayCode.Services;

namespace ArayCode;

/// <summary>
/// Maps exceptions to process exit codes and orchestrates the shutdown user experience
/// (error messages, key-press waits).
/// 
/// Resilience: when StreamShell is unreachable (already stopped, disposed, or crashed),
/// falls back to raw <see cref="Console"/> output so the user always sees the error
/// before the terminal window closes.
/// </summary>
public sealed class AppExitHandler : IDisposable
{
    /// <summary>Returned when the application exits normally after cancellation.</summary>
    public const int ExitCancelled = 0;

    /// <summary>Returned when the application exits due to a handled error.</summary>
    public const int ExitError = 1;

    private readonly IColorConsole _console;
    private bool _streamShellDead;

    public AppExitHandler(IColorConsole console)
    {
        _console = console;
    }

    /// <summary>
    /// Handles <paramref name="ex"/> and returns the appropriate process exit code.
    /// </summary>
    public int HandleExit(Exception? ex)
    {
        try
        {
            return HandleExitInternal(ex);
        }
        catch
        {
            // If even the error handler itself throws (e.g. StreamShell disposed
            // while we're writing to it), fall back to raw console.
            _streamShellDead = true;
            return HandleExitInternal(ex);
        }
    }

    private int HandleExitInternal(Exception? ex)
    {
        switch (ex)
        {
            case OperationCanceledException:
                return ExitCancelled;

            case GatewayException gex:
                PrintError($"Gateway error: {gex.Message}");
                if (gex.DetailCode != null)
                    PrintError($"  Detail: {gex.DetailCode}");
                if (gex.RecommendedStep != null)
                    PrintError($"  Recommendation: {gex.RecommendedStep}");
                WaitForUser();
                return ExitError;

            case Exception ex2:
                PrintError($"Fatal: {ex2.Message}");
                PrintError(ex2.StackTrace ?? "(no stack trace)");
                if (ex2.InnerException != null)
                    PrintError($"Inner: {ex2.InnerException.GetType().Name}: {ex2.InnerException.Message}");
                WaitForUser();
                return ExitError;

            default:
                return ExitCancelled;
        }
    }

    private void PrintError(string message)
    {
        if (_streamShellDead)
        {
            FallbackConsoleWrite(message);
            return;
        }

        try
        {
            _console.PrintError(message);
        }
        catch
        {
            _streamShellDead = true;
            FallbackConsoleWrite(message);
        }
    }

    private static void FallbackConsoleWrite(string message)
    {
        try
        {
            Console.Error.WriteLine(message);
        }
        catch
        {
            // Last resort — nothing we can do.
        }
    }

    /// <summary>
    /// Waits for the user to acknowledge the error before the terminal closes.
    /// Uses a blocking <see cref="Console.ReadLine"/> — we want the window to stay
    /// open until the user presses Enter.
    /// </summary>
    private static void WaitForUser()
    {
        try
        {
            Console.Write("Press Enter to close...");
            Console.ReadLine();
        }
        catch (InvalidOperationException)
        {
            // No console or stdin redirected — can't wait, just exit.
        }
    }

    public void Dispose() { }
}

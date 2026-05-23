using ArayCode.Services;

namespace ArayCode;

/// <summary>
/// ILogger implementation using IColorConsole for output.
/// </summary>
public sealed class ConsoleLogger : ILogger
{
    private readonly IColorConsole _console;

    /// <summary>
    /// Creates a new ConsoleLogger instance with the specified IColorConsole.
    /// </summary>
    public ConsoleLogger(IColorConsole console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    /// <inheritdoc />
    public void Log(string source, string message, LogLevel level = LogLevel.Debug)
    {
        _console.Log(source, message, level);
    }

    /// <inheritdoc />
    public void LogError(string source, string message, LogLevel level = LogLevel.Error)
    {
        _console.LogError(source, message);
    }
}

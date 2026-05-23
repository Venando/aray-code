namespace ArayCode;

/// <summary>
/// Simple logging abstraction for diagnostic output.
/// </summary>
public interface ILogger
{
    /// <summary>Logs a message with the specified severity level.</summary>
    void Log(string source, string message, LogLevel level = LogLevel.Debug);

    /// <summary>Logs an error message.</summary>
    void LogError(string source, string message, LogLevel level = LogLevel.Error);
}

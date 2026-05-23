namespace ArayCode;

/// <summary>
/// Controls which diagnostic log messages are emitted to the console.
/// Higher values include all lower levels.
/// </summary>
public enum LogLevel
{
    /// <summary>No diagnostic log output at all.</summary>
    None = 0,

    /// <summary>Only error messages (default).</summary>
    Error = 1,

    /// <summary>Error messages and informational status updates.</summary>
    Info = 2,

    /// <summary>Detailed debug information for development.</summary>
    Debug = 3,

    /// <summary>Verbose trace-level output (e.g. full payload dumps).</summary>
    Verbose = 4,
}

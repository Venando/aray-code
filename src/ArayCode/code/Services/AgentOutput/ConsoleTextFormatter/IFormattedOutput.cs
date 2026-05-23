namespace ArayCode;

/// <summary>
/// Minimal output abstraction for AgentReplyFormatter.
/// Replaces IConsole which was too broad (had raw I/O + color state).
/// </summary>
public interface IFormattedOutput
{
    void Write(string text);
    void WriteLine();
    int WindowWidth { get; }
}

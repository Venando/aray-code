namespace OpenClawPTT.Services.Commands;

/// <summary>
/// Event data raised whenever any command (native or OpenClaw) is executed.
/// Replaces the previous <c>Action&lt;string&gt;</c> event to carry richer metadata.
/// </summary>
public sealed class CommandExecutedEventArgs : EventArgs
{
    /// <summary>The command name without leading slash (e.g. "quit", "reset").</summary>
    public string Name { get; }

    /// <summary>Where the command originated from.</summary>
    public CommandSource Source { get; }

    /// <summary>The functional classification of the command.</summary>
    public ShellCommandType Type { get; }

    /// <summary>Positional arguments passed to the command.</summary>
    public string[] Args { get; }

    /// <summary>Named arguments (key=value) passed to the command.</summary>
    public Dictionary<string, string> NamedArgs { get; }

    public CommandExecutedEventArgs(
        string name,
        CommandSource source,
        ShellCommandType type,
        string[] args,
        Dictionary<string, string> namedArgs)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Source = source;
        Type = type;
        Args = args ?? Array.Empty<string>();
        NamedArgs = namedArgs ?? new Dictionary<string, string>();
    }
}

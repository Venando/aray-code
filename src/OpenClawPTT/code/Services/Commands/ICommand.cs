namespace OpenClawPTT.Services.Commands;

/// <summary>
/// Common interface for all PTT commands — both native and OpenClaw-forwarded.
/// Each command is a self-contained unit with its own name, description, metadata, and execution logic.
/// </summary>
public interface ICommand
{
    /// <summary>Command name without leading slash (e.g. "quit", "reset").</summary>
    string Name { get; }

    /// <summary>Human-readable description shown in command help.</summary>
    string Description { get; }

    /// <summary>Where the command originates from.</summary>
    CommandSource Source { get; }

    /// <summary>Functional classification of the command.</summary>
    CommandType Type { get; }

    /// <summary>Optional argument suggestions for tab completion. Null when none.</summary>
    string[]? Suggestions { get; }

    /// <summary>Execute the command with the given arguments.</summary>
    Task ExecuteAsync(string[] args, Dictionary<string, string> namedArgs, CancellationToken ct = default);
}

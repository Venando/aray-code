public sealed class CommandMetadata
{
    /// <summary>The raw command string, trimmed of surrounding whitespace.</summary>
    public string Raw { get; init; } = string.Empty;

    /// <summary>The executable / primary binary (first token).</summary>
    public string Executable { get; init; } = string.Empty;

    /// <summary>All arguments after the executable.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Flags/switches (tokens starting with - or --).</summary>
    public IReadOnlyList<string> Flags { get; init; } = [];

    /// <summary>Positional arguments (non-flag tokens after the executable).</summary>
    public IReadOnlyList<string> Positionals { get; init; } = [];

    /// <summary>Classified type of command.</summary>
    public CommandType Type { get; init; }

    /// <summary>Working-directory hint when the command is prefixed with cd X &&.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Redirect target file, e.g. "2>&1", "/tmp/out.log".</summary>
    public IReadOnlyList<string> Redirects { get; init; } = [];

    /// <summary>True when command is part of a pipeline (contains |).</summary>
    public bool IsPiped { get; init; }

    /// <summary>True when command is chained with && or ||.</summary>
    public bool IsChained { get; init; }

    /// <summary>For here-doc commands: the delimiter and the embedded body.</summary>
    public HereDocInfo? HereDoc { get; init; }

    /// <summary>Environment variables set inline, e.g. FOO=bar cmd.</summary>
    public IReadOnlyDictionary<string, string> InlineEnv { get; init; }
        = new Dictionary<string, string>();

    /// <summary>For script commands: the inline script body (e.g. python -c "...").</summary>
    public string? ScriptBody { get; init; }

    public override string ToString() =>
        $"[{Type}] {Executable}  args={Arguments.Count}  flags={string.Join(",", Flags)}";
}

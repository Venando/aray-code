namespace ArayCode.Services;

/// <summary>
/// Represents a single entry in a diff result.
/// </summary>
/// <param name="Operation">The type of diff operation.</param>
/// <param name="Line">The text content of the line.</param>
/// <param name="OldLineNumber">The line number in the old text (null for additions).</param>
/// <param name="NewLineNumber">The line number in the new text (null for removals).</param>
public sealed record DiffEntry(
    DiffOperation Operation,
    string Line,
    int? OldLineNumber = null,
    int? NewLineNumber = null);

namespace ArayCode.Services;

/// <summary>
/// Represents the result of a line-based diff computation.
/// </summary>
/// <param name="Entries">The ordered list of diff entries.</param>
/// <param name="Additions">The number of added lines.</param>
/// <param name="Removals">The number of removed lines.</param>
/// <param name="Changes">The number of changed lines (min of additions and removals).</param>
public sealed record DiffResult(
    List<DiffEntry> Entries,
    int Additions,
    int Removals,
    int Changes)
{
    /// <summary>
    /// Returns true if there are no differences.
    /// </summary>
    public bool IsEmpty => Entries.Count == 0 ||
                          (Additions == 0 && Removals == 0 && Entries.All(e => e.Operation == DiffOperation.Equal));
}

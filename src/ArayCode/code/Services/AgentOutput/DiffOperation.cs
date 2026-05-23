namespace ArayCode.Services;

/// <summary>
/// Represents the type of operation in a diff entry.
/// </summary>
public enum DiffOperation
{
    /// <summary>
    /// Line is unchanged between old and new versions.
    /// </summary>
    Equal,

    /// <summary>
    /// Line was added in the new version.
    /// </summary>
    Add,

    /// <summary>
    /// Line was removed in the new version.
    /// </summary>
    Remove
}

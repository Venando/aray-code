namespace ArayCode.Services;

/// <summary>
/// Represents a single discrete status info element (active agent name,
/// model, thinking level, token context, conversation name, connection
/// status) that can be independently positioned and cached.
/// </summary>
public interface IStatusPart
{
    /// <summary>
    /// Returns the current rendered Spectre-markup text for this part.
    /// Returns a cached string when <see cref="IsDirty"/> is false.
    /// </summary>
    string GetText();

    /// <summary>
    /// True if the underlying data has changed since the last call to
    /// <see cref="MarkClean"/>, meaning the cached text is stale.
    /// </summary>
    bool IsDirty { get; }

    /// <summary>Resets the dirty flag after the rendered text has been consumed by the UI.</summary>
    void MarkClean();

    /// <summary>Marks this part dirty so the next <see cref="GetText()"/> call rebuilds the cache.</summary>
    void MarkDirty();

    /// <summary>Where this status part should be displayed.</summary>
    DisplayPosition Position { get; set; }

    /// <summary>
    /// Sort order within the same <see cref="Position"/> group.
    /// Lower values appear first (e.g. 0 → agent name, 10 → model, 20 → thinking level).
    /// </summary>
    int Order { get; set; }

    /// <summary>
    /// Separator text to insert before this part when it is NOT the first
    /// part rendered at its <see cref="Position"/>.
    /// E.g. " · " for model/thinking/context, " " for conversation name, "" for agent name.
    /// </summary>
    string SeparatorBefore { get; }
}

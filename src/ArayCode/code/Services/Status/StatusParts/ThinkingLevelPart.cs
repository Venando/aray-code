namespace ArayCode.Services.StatusParts;

/// <summary>
/// Renders the active agent's thinking level, e.g. "high".
/// Inherits standard dirty-tracking and text caching from
/// <see cref="StringStatusPartBase"/>.
/// </summary>
public sealed class ThinkingLevelPart : StringStatusPartBase
{
    public ThinkingLevelPart(DisplayPosition defaultPosition = DisplayPosition.TopSeparatorLeft, int order = 20)
        : base(defaultPosition, order, " · ")
    {
    }
}

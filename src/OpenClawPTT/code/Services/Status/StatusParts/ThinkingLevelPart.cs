namespace OpenClawPTT.Services.StatusParts;

/// <summary>
/// Renders the active agent's thinking level, e.g. "high".
/// </summary>
public sealed class ThinkingLevelPart : StatusPartBase
{
    private string? _thinkingLevel;

    public ThinkingLevelPart(DisplayPosition defaultPosition = DisplayPosition.TopSeparatorLeft, int order = 20)
        : base(defaultPosition, order)
    {
    }

    /// <inheritdoc />
    public override string SeparatorBefore => " · ";

    /// <summary>Feeds a new thinking level. Marks dirty only on actual value change.</summary>
    public void Update(string? thinkingLevel)
    {
        if (!string.Equals(_thinkingLevel, thinkingLevel, StringComparison.Ordinal))
        {
            _thinkingLevel = thinkingLevel;
            MarkDirty();
        }
    }

    protected override void BuildText()
    {
        if (string.IsNullOrEmpty(_thinkingLevel))
            return;

        Builder.Append(_thinkingLevel);
    }
}

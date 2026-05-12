namespace OpenClawPTT.Services.StatusParts;

/// <summary>
/// Renders the conversation / session name, e.g. "│ WireResetCheck │".
/// Caches the rendered value so it only rebuilds on actual name changes.
/// </summary>
public sealed class ConversationNamePart : StatusPartBase
{
    private const string RepeatedCharacterMarkup = "white";
    private const string ConversationNameMarkup = $"italic {RepeatedCharacterMarkup}";
    private const string ConvOpen = "[grey]\u2502[/] [" + ConversationNameMarkup + "]";
    private const string ConvClose = "[/] [grey]\u2502[/]";

    private string? _conversationName;

    public ConversationNamePart(DisplayPosition defaultPosition = DisplayPosition.TopSeparatorLeft, int order = 40)
        : base(defaultPosition, order)
    {
    }

    /// <inheritdoc />
    public override string SeparatorBefore => " ";

    /// <summary>Feeds a new conversation name. Pass null to clear. Marks dirty on actual change.</summary>
    public void Update(string? conversationName)
    {
        if (!string.Equals(_conversationName, conversationName, StringComparison.Ordinal))
        {
            _conversationName = conversationName;
            MarkDirty();
        }
    }

    protected override void BuildText()
    {
        if (string.IsNullOrWhiteSpace(_conversationName))
            return;

        Builder.Append(ConvOpen);
        Builder.Append(_conversationName);
        Builder.Append(ConvClose);
    }
}

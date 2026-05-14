using OpenClawPTT.Services.Themes;

namespace OpenClawPTT.Services.StatusParts;

/// <summary>
/// Renders the conversation / session name, e.g. "│ WireResetCheck │".
/// Caches the rendered value so it only rebuilds on actual name changes.
/// Styles driven from <see cref="ThemeProvider.Current.Tools"/>.
/// </summary>
public sealed class ConversationNamePart : StringStatusPartBase
{
    public ConversationNamePart(DisplayPosition defaultPosition = DisplayPosition.TopSeparatorLeft, int order = 40)
        : base(defaultPosition, order, " ")
    {
    }

    protected override void BuildText()
    {
        var name = Value;
        if (string.IsNullOrWhiteSpace(name))
            return;

        var tools = ThemeProvider.Current.Tools;
        var pipeMarkup = tools.StatusBar.VerticalPipe;
        var nameMarkup = tools.StatusBar.ConversationNameStyle;

        Builder.Append($"[{pipeMarkup}]│[/] [{nameMarkup}]{name}[/] [{pipeMarkup}]│[/]");
    }
}

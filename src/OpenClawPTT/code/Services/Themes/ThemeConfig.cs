namespace OpenClawPTT.Services.Themes;

/// <summary>
/// Represents a complete theme configuration with all style domains.
/// Properties are mutable so a single instance can be updated at runtime
/// via <see cref="ThemeProvider.Current"/>.
/// </summary>
public sealed class ThemeConfig
{
    public string Name { get; set; } = "Default";
    public string Author { get; set; } = "OpenClaw PTT";
    public MarkdownTheme Markdown { get; set; } = new();
    public TableTheme Table { get; set; } = new();
    public ToolTheme Tools { get; set; } = new();
    public static ThemeConfig Default => new();
}

/// <summary>
/// Styles for Markdown block-level and inline rendering.
/// Each string is a Spectre.Console markup style tag (without the brackets),
/// e.g. <c>"bold gray89 on darkblue"</c> or <c>"dim"</c>.
/// </summary>
public sealed class MarkdownTheme
{
    public string CodeFenceStartMarkup { get; set; } = "[dim]\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500[italic]code[/]\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500[/]";
    public string CodeFenceEndMarkup { get; set; } = "[dim]\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500[/]";
    public string CodeContentStyle { get; set; } = "default on gray15";
    public string InlineCodeStyle { get; set; } = "bold gray89 on darkblue";
    public string HeadingH1Style { get; set; } = "bold underline default on gray27";
    public string HeadingH2Style { get; set; } = "bold underline";
    public string HeadingH3PlusStyle { get; set; } = "bold dim";
    public string BlockquoteStyle { get; set; } = "italic dim";
    public string ThematicBreakMarkup { get; set; } = "[dim]\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500[/]";
    public string BoldItalicStyle { get; set; } = "bold italic";
    public string BoldStyle { get; set; } = "bold";
    public string ItalicStyle { get; set; } = "italic";
    public string StrikethroughStyle { get; set; } = "strikethrough";
}

public sealed class TableTheme
{
    public string EdgeColor { get; set; } = "deepskyblue3";
}

/// <summary>
/// Styles for tool rendering (tool call display in the console).
/// Properties are grouped into nested classes by domain.
/// Each string is a Spectre.Console style (e.g. "grey", "bold cyan").
/// </summary>
public sealed class ToolTheme
{
    public string HeaderStyle { get; set; } = "gray93 on #333333";
    public GeneralStyles General { get; set; } = new();
    public KvpStyles Kvp { get; set; } = new();
    public ExecStyles Exec { get; set; } = new();
    public DiffStyles Diff { get; set; } = new();
    public ReaderStyles Reader { get; set; } = new();
    public StatusBarStyles StatusBar { get; set; } = new();
    public ThinkingStyles Thinking { get; set; } = new();
    public MessageStyles Messages { get; set; } = new();
    public PanelStyles Panel { get; set; } = new();
    public StreamShellStyles StreamShell { get; set; } = new();
}

public sealed class GeneralStyles
{
    public string Label { get; set; } = "grey";
    public string Muted { get; set; } = "grey";
    public string Value { get; set; } = "white";
    public string Separator { get; set; } = "grey";
    public string MutedSeparator { get; set; } = "black";
    public string TruncatedMore { get; set; } = "grey";
}

public sealed class KvpStyles
{
    public string Separator { get; set; } = "grey";
    public string Key { get; set; } = "grey";
    public string Value { get; set; } = "white";
    public string Label { get; set; } = "grey";
}

public sealed class ExecStyles
{
    public string FileSystem { get; set; } = "green";
    public string FileContent { get; set; } = "blue";
    public string Build { get; set; } = "magenta";
    public string PackageManager { get; set; } = "red";
    public string Network { get; set; } = "cyan";
    public string Scripting { get; set; } = "yellow";
    public string Process { get; set; } = "olive";
    public string HereDoc { get; set; } = "darkcyan";
    public string Vcs { get; set; } = "olive";
    public string Positional { get; set; } = "cyan";
    public string LongFlag { get; set; } = "green";
    public string ShortFlag { get; set; } = "olive";
    public string EnvKey { get; set; } = "cyan";
    public string EnvValue { get; set; } = "yellow";
    public string ScriptBody { get; set; } = "grey";
    public string HereDocSummary { get; set; } = "grey";
    public string PathIcon { get; set; } = "grey";
    public string PathText { get; set; } = "grey";
}

public sealed class DiffStyles
{
    public string Added { get; set; } = "default on springgreen4";
    public string Removed { get; set; } = "default on darkred";
    public string Prefix { get; set; } = "grey";
}

public sealed class ReaderStyles
{
    public string LineInfo { get; set; } = "grey";
    public string FetchUrl { get; set; } = "grey";
    public string FetchMaxInfo { get; set; } = "grey";
}

public sealed class StatusBarStyles
{
    public string SeparatorChar { get; set; } = "─";
    public string SeparatorCharMarkup { get; set; } = "white";
    public string TopLeftSeparatorPrefix { get; set; } = "──────────────── ";
    public string VerticalPipe { get; set; } = "grey";
    public string SegmentPipe { get; set; } = "white bold";
    public string NoAgentsText { get; set; } = "grey";
    public string ConversationNameStyle { get; set; } = "italic white";
    public string UserMessagePrefix { get; set; } = "[green] Me:[/] ";
}

public sealed class ThinkingStyles
{
    public string HeaderStyle { get; set; } = "gray93 on #333333";
    public string TextStyle { get; set; } = "grey";
    public string MoreStyle { get; set; } = "dim";
}

public sealed class MessageStyles
{
    public string BannerBorder { get; set; } = "deepskyblue3";
    public string HelpCommand { get; set; } = "grey";
    public string Info { get; set; } = "grey";
    public string Highlight { get; set; } = "cyan2";
    public string Emphasis { get; set; } = "bold";
    public string Success { get; set; } = "green";
    public string Warning { get; set; } = "yellow";
    public string Error { get; set; } = "red";
    public string RecordingIndicator { get; set; } = "red";
    public string GatewayError { get; set; } = "red";
    public string LogTag { get; set; } = "grey";
    public string LogOk { get; set; } = "green";
    public string LogError { get; set; } = "red";
    public string FallbackWarning { get; set; } = "orange1";
    public string FallbackFrom { get; set; } = "red";
    public string FallbackTo { get; set; } = "green";
    public string ModelFailed { get; set; } = "red";
    public string AgentBadge { get; set; } = "white on gray15";
    public string IntroductionBorder { get; set; } = "deepskyblue3";
    public string PanelCap { get; set; } = "white";
}

public sealed class PanelStyles
{
    public string Hint { get; set; } = "dim grey";
    public string SelectedBg { get; set; } = "Grey84";
    public string SelectedName { get; set; } = "bold black";
    public string Action { get; set; } = "grey";
    public string ActionSelected { get; set; } = "Grey23";
    public string Time { get; set; } = "grey42";
    public string SectionHeader { get; set; } = "bold cyan2";
}

public sealed class StreamShellStyles
{
    public string CursorMarkup { get; set; } = "bold black on cyan";
    public string SelectionMarkup { get; set; } = "bold cyan on Grey27";
    public string CommandSlashMarkup { get; set; } = "Red1";
    public string InputPrefixStyle { get; set; } = "bold SkyBlue1";
}

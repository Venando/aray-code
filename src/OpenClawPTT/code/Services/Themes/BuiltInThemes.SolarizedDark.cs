namespace OpenClawPTT.Services.Themes;

public static partial class BuiltInThemes
{
    // ── Solarized Dark ────────────────────────────────────────────────

    /// <summary>Solarized Dark — earthy base02/base03 palette with teal/blue accents.</summary>
    public static ThemeConfig SolarizedDark => new()
    {
        Name = "Solarized Dark",
        Author = "OpenClaw PTT",
        Markdown = new MarkdownTheme
        {
            CodeContentStyle = "#839496 on #002b36",
            InlineCodeStyle = "bold #839496 on #073642",
            HeadingH1Style = "bold underline #268bd2",
            HeadingH2Style = "bold underline #859900",
            HeadingH3PlusStyle = "bold #d33682",
            BlockquoteStyle = "italic #586e75",
            BoldItalicStyle = "bold italic #d33682",
            BoldStyle = "bold #d33682",
            ItalicStyle = "italic #839496",
            StrikethroughStyle = "strikethrough #586e75",
        },
        Table = new TableTheme { EdgeColor = "#2aa198" },
        Tools = SolarizedTools,
        Palette = new PaletteStyle
        {
            SelectedStyle = "on #073642",
            SelectedCursorColor = "#93a1a1",
            SelectedCursorSymbol = "→",
            SelectedNameColor = "#93a1a1",
            SelectedDescriptionColor = "#586e75",
            NormalIndent = " ",
            NormalNameColor = "#2aa198",
            NavigationHintStyle = "dim #586e75",
        },
    };

    private static readonly ToolTheme SolarizedTools = new()
    {
        HeaderStyle = "bold #839496 on #073642",
        General = new GeneralStyles
        {
            Label = "#839496", Muted = "#586e75", Value = "#839496",
            Separator = "#586e75", MutedSeparator = "#073642", TruncatedMore = "#586e75",
        },
        Kvp = new KvpStyles
        {
            Separator = "#586e75", Key = "#586e75", Value = "#839496", Label = "#586e75",
        },
        Exec = new ExecStyles
        {
            FileSystem = "#859900", FileContent = "#268bd2", Build = "#d33682",
            PackageManager = "#dc322f", Network = "#268bd2", Scripting = "#b58900",
            Process = "#cb4b16", HereDoc = "#586e75", Vcs = "#cb4b16",
            Positional = "#268bd2", LongFlag = "#859900", ShortFlag = "#cb4b16",
            EnvKey = "#268bd2", EnvValue = "#b58900", ScriptBody = "#586e75",
            HereDocSummary = "#586e75", PathIcon = "#586e75", PathText = "#586e75",
        },
        Diff = new DiffStyles
        {
            Added = "#839496 on #1b3b1b", Removed = "#839496 on #5a1d1d", Prefix = "#586e75",
        },
        Reader = new ReaderStyles
        {
            LineInfo = "#586e75", FetchUrl = "#839496", FetchMaxInfo = "#586e75",
        },
        StatusBar = new StatusBarStyles
        {
            SeparatorChar = "\u2500", SeparatorCharMarkup = "#586e75",
            TopLeftSeparatorPrefix = "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500 ",
            VerticalPipe = "#586e75", SegmentPipe = "#586e75 bold",
            NoAgentsText = "#586e75", ConversationNameStyle = "italic #2aa198",
            UserMessagePrefix = "[#859900] Me:[/] ",
        },
        Thinking = new ThinkingStyles
        {
            HeaderStyle = "bold #839496 on #073642", TextStyle = "#839496", MoreStyle = "#586e75",
        },
        Messages = new MessageStyles
        {
            BannerBorder = "#268bd2", HelpCommand = "#586e75",
            Info = "#586e75", Highlight = "#2aa198", Emphasis = "bold",
            Success = "#859900", Warning = "#b58900", Error = "#dc322f",
            RecordingIndicator = "#dc322f", GatewayError = "#dc322f",
            LogTag = "#586e75", LogOk = "#859900", LogError = "#dc322f",
            FallbackWarning = "#b58900", FallbackFrom = "#dc322f", FallbackTo = "#859900",
            ModelFailed = "#dc322f", AgentBadge = "#839496 on #073642", IntroductionBorder = "#268bd2",
            PanelCap = "#586e75", Working = "#b58900",
        },
        Panel = new PanelStyles
        {
            Hint = "#586e75", SelectedBg = "#073642",
            SelectedName = "bold #93a1a1", Action = "#586e75",
            ActionSelected = "#93a1a1", Time = "#586e75",
            SectionHeader = "bold #2aa198",
            ActiveName = "underline", ActiveAgentAction = "underline",
        },
        StreamShell = new StreamShellStyles
        {
            CursorMarkup = "bold #002b36 on #93a1a1", SelectionMarkup = "bold #93a1a1 on #073642",
            CommandSlashMarkup = "#d33682", NativeCommandNameStyle = "#b58900", InputPrefixStyle = "bold #268bd2",
        },
    };
}

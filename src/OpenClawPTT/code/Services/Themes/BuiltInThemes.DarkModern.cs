namespace OpenClawPTT.Services.Themes;

public static partial class BuiltInThemes
{
    // ── Dark Modern ──────────────────────────────────────────────────

    /// <summary>VS Code Dark Modern — clean blue-on-dark-gray palette.</summary>
    public static ThemeConfig DarkModern => new()
    {
        Name = "Dark Modern",
        Author = "OpenClaw PTT",
        Markdown = new MarkdownTheme
        {
            CodeContentStyle = "#cccccc on #1e1e1e",
            InlineCodeStyle = "bold #cccccc on #3c3c3c",
            HeadingH1Style = "bold underline #569cd6",
            HeadingH2Style = "bold underline #4ec9b0",
            HeadingH3PlusStyle = "bold #c586c0",
            BlockquoteStyle = "italic #808080",
            BoldItalicStyle = "bold italic #c586c0",
            BoldStyle = "bold #c586c0",
            ItalicStyle = "italic #cccccc",
            StrikethroughStyle = "strikethrough #808080",
        },
        Table = new TableTheme { EdgeColor = "#569cd6" },
        Tools = DarkModernTools,
        Palette = new PaletteStyle
        {
            SelectedBackground = "on #3c3c3c",
            SelectedCursorColor = "#cccccc",
            SelectedCursorSymbol = "→",
            SelectedNameColor = "#cccccc",
            SelectedDescriptionColor = "#808080",
            NormalIndent = " ",
            NormalNameColor = "#569cd6",
            NavigationHintStyle = "dim #808080",
        },
    };

    private static readonly ToolTheme DarkModernTools = new()
    {
        HeaderStyle = "bold #cccccc on #2d2d2d",
        General = new GeneralStyles
        {
            Label = "#cccccc", Muted = "#808080", Value = "#cccccc",
            Separator = "#808080", MutedSeparator = "#2d2d2d", TruncatedMore = "#808080",
        },
        Kvp = new KvpStyles
        {
            Separator = "#808080", Key = "#808080", Value = "#cccccc", Label = "#808080",
        },
        Exec = new ExecStyles
        {
            FileSystem = "#4ec9b0", FileContent = "#569cd6", Build = "#c586c0",
            PackageManager = "#d16969", Network = "#569cd6", Scripting = "#dcdcaa",
            Process = "#d7ba7d", HereDoc = "#808080", Vcs = "#d7ba7d",
            Positional = "#569cd6", LongFlag = "#4ec9b0", ShortFlag = "#d7ba7d",
            EnvKey = "#569cd6", EnvValue = "#dcdcaa", ScriptBody = "#808080",
            HereDocSummary = "#808080", PathIcon = "#808080", PathText = "#808080",
        },
        Diff = new DiffStyles
        {
            Added = "#cccccc on #1b4d1b", Removed = "#cccccc on #5a1d1d", Prefix = "#808080",
        },
        Reader = new ReaderStyles
        {
            LineInfo = "#808080", FetchUrl = "#cccccc", FetchMaxInfo = "#808080",
        },
        StatusBar = new StatusBarStyles
        {
            SeparatorChar = "\u2500", SeparatorCharMarkup = "#808080",
            TopLeftSeparatorPrefix = "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500 ",
            VerticalPipe = "#808080", SegmentPipe = "#808080 bold",
            NoAgentsText = "#808080", ConversationNameStyle = "italic #569cd6",
            UserMessagePrefix = "[#4ec9b0] Me:[/] ",
        },
        Thinking = new ThinkingStyles
        {
            HeaderStyle = "bold #cccccc on #2d2d2d", TextStyle = "#cccccc", MoreStyle = "#808080",
        },
        Messages = new MessageStyles
        {
            BannerBorder = "#569cd6", HelpCommand = "#808080",
            Info = "#808080", Highlight = "#569cd6", Emphasis = "bold",
            Success = "#4ec9b0", Warning = "#dcdcaa", Error = "#d16969",
            RecordingIndicator = "#d16969", GatewayError = "#d16969",
            LogTag = "#808080", LogOk = "#4ec9b0", LogError = "#d16969",
            FallbackWarning = "#dcdcaa", FallbackFrom = "#d16969", FallbackTo = "#4ec9b0",
            ModelFailed = "#d16969", AgentBadge = "#cccccc on #2d2d2d", IntroductionBorder = "#569cd6",
            PanelCap = "#808080", Working = "#dcdcaa",
        },
        Panel = new PanelStyles
        {
            Hint = "#808080", SelectedBg = "#3c3c3c",
            SelectedName = "bold #cccccc", Action = "#808080",
            ActionSelected = "#cccccc", Time = "#808080",
            SectionHeader = "bold #569cd6",
            ActiveName = "underline", ActiveAgentAction = "underline",
        },
        StreamShell = new StreamShellStyles
        {
            CursorMarkup = "bold #1e1e1e on #cccccc", SelectionMarkup = "bold #cccccc on #3c3c3c",
            CommandSlashMarkup = "#c586c0", NativeCommandNameStyle = "#dcdcaa", InputPrefixStyle = "bold #569cd6",
        },
    };
}

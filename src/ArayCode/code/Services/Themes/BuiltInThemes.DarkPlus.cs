namespace ArayCode.Services.Themes;

public static partial class BuiltInThemes
{
    // ── Dark+ ─────────────────────────────────────────────────────────

    /// <summary>VS Code Dark+ — vibrant version with punchier accents.</summary>
    public static ThemeConfig DarkPlus => new()
    {
        Name = "Dark+",
        Author = "ArayCode",
        Markdown = new MarkdownTheme
        {
            HeadingH1Style = "bold underline #569cd6",
            HeadingH2Style = "bold underline #4ec9b0",
            HeadingH3PlusStyle = "bold #c586c0",
            InlineCodeStyle = "bold #d4d4d4 on #3c3c3c",
        },
        Table = new TableTheme { EdgeColor = "#569cd6" },
        Tools = DarkPlusTools,
        Palette = new PaletteStyle
        {
            SelectedBackground = "on #264f78",
            SelectedCursorColor = "#d4d4d4",
            SelectedCursorSymbol = "→",
            SelectedNameColor = "#d4d4d4",
            SelectedDescriptionColor = "#808080",
            NormalIndent = " ",
            NormalNameColor = "#569cd6",
            NavigationHintStyle = "dim #808080",
        },
    };

    private static readonly ToolTheme DarkPlusTools = new()
    {
        HeaderStyle = "bold #d4d4d4 on #333333",
        General = new GeneralStyles
        {
            Label = "#d4d4d4", Muted = "#808080", Value = "#d4d4d4",
            Separator = "#808080", MutedSeparator = "#333333", TruncatedMore = "#808080",
        },
        Kvp = new KvpStyles
        {
            Separator = "#808080", Key = "#808080", Value = "#d4d4d4", Label = "#808080",
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
            Added = "#d4d4d4 on #1b4d1b", Removed = "#d4d4d4 on #5a1d1d", Prefix = "#808080",
        },
        Reader = new ReaderStyles
        {
            LineInfo = "#808080", FetchUrl = "#d4d4d4", FetchMaxInfo = "#808080",
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
            HeaderStyle = "bold #d4d4d4 on #333333", TextStyle = "#d4d4d4", MoreStyle = "#808080",
        },
        Messages = new MessageStyles
        {
            BannerBorder = "#569cd6", HelpCommand = "#808080",
            Info = "#808080", Highlight = "#569cd6", Emphasis = "bold",
            Success = "#4ec9b0", Warning = "#dcdcaa", Error = "#d16969",
            RecordingIndicator = "#d16969", GatewayError = "#d16969",
            LogTag = "#808080", LogOk = "#4ec9b0", LogError = "#d16969",
            FallbackWarning = "#dcdcaa", FallbackFrom = "#d16969", FallbackTo = "#4ec9b0",
            ModelFailed = "#d16969", AgentBadge = "#d4d4d4 on #3c3c3c", IntroductionBorder = "#569cd6",
            PanelCap = "#808080", Working = "#dcdcaa",
            ForwardedCommandStyle = "#569cd6 on #333333",
        },
        Panel = new PanelStyles
        {
            Hint = "#808080", SelectedBg = "#04395e",
            SelectedName = "bold #d4d4d4", Action = "#808080",
            ActionSelected = "#d4d4d4", Time = "#808080",
            SectionHeader = "bold #569cd6",
            ActiveName = "underline", ActiveAgentAction = "underline",
        },
        StreamShell = new StreamShellStyles
        {
            CursorMarkup = "bold #1e1e1e on #d4d4d4", SelectionMarkup = "bold #d4d4d4 on #264f78",
            CommandSlashMarkup = "#c586c0", NativeCommandNameStyle = "#dcdcaa", InputPrefixStyle = "bold #569cd6",
        },
    };
}

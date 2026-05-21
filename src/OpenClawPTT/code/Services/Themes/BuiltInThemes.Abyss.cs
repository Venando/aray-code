namespace OpenClawPTT.Services.Themes;

public static partial class BuiltInThemes
{
    // ── Abyss ─────────────────────────────────────────────────────────

    /// <summary>Abyss — deep blue-black background, light blue accents, limited palette.</summary>
    public static ThemeConfig Abyss => new()
    {
        Name = "Abyss",
        Author = "OpenClaw PTT",
        Markdown = new MarkdownTheme
        {
            CodeContentStyle = "#c0c0c0 on #000c18",
            InlineCodeStyle = "bold #c0c0c0 on #0a1a2a",
            HeadingH1Style = "bold underline #80a0ff",
            HeadingH2Style = "bold underline #c080a0",
            HeadingH3PlusStyle = "bold #eaeaea",
            BlockquoteStyle = "italic #5a6a7a",
            BoldItalicStyle = "bold italic #80a0ff",
            BoldStyle = "bold #80a0ff",
            ItalicStyle = "italic #c0c0c0",
            StrikethroughStyle = "strikethrough #5a6a7a",
        },
        Table = new TableTheme { EdgeColor = "#80a0ff" },
        Tools = AbyssTools,
        Palette = new PaletteStyle
        {
            SelectedStyle = "on #0a1a2a",
            SelectedCursorColor = "#c0c0c0",
            SelectedCursorSymbol = "→",
            SelectedNameColor = "#c0c0c0",
            SelectedDescriptionColor = "#5a6a7a",
            NormalIndent = " ",
            NormalNameColor = "#80a0ff",
            NavigationHintStyle = "dim #5a6a7a",
        },
    };

    private static readonly ToolTheme AbyssTools = new()
    {
        HeaderStyle = "bold #c0c0c0 on #0a1a2a",
        General = new GeneralStyles
        {
            Label = "#c0c0c0", Muted = "#5a6a7a", Value = "#c0c0c0",
            Separator = "#5a6a7a", MutedSeparator = "#0a1a2a", TruncatedMore = "#5a6a7a",
        },
        Kvp = new KvpStyles
        {
            Separator = "#5a6a7a", Key = "#5a6a7a", Value = "#c0c0c0", Label = "#5a6a7a",
        },
        Exec = new ExecStyles
        {
            FileSystem = "#c080a0", FileContent = "#80a0ff", Build = "#eaeaea",
            PackageManager = "#c080a0", Network = "#80a0ff", Scripting = "#eaeaea",
            Process = "#c080a0", HereDoc = "#5a6a7a", Vcs = "#c080a0",
            Positional = "#80a0ff", LongFlag = "#c080a0", ShortFlag = "#c080a0",
            EnvKey = "#80a0ff", EnvValue = "#eaeaea", ScriptBody = "#5a6a7a",
            HereDocSummary = "#5a6a7a", PathIcon = "#5a6a7a", PathText = "#5a6a7a",
        },
        Diff = new DiffStyles
        {
            Added = "#c0c0c0 on #1b3b5a", Removed = "#c0c0c0 on #5a2a3a", Prefix = "#5a6a7a",
        },
        Reader = new ReaderStyles
        {
            LineInfo = "#5a6a7a", FetchUrl = "#c0c0c0", FetchMaxInfo = "#5a6a7a",
        },
        StatusBar = new StatusBarStyles
        {
            SeparatorChar = "\u2500", SeparatorCharMarkup = "#5a6a7a",
            TopLeftSeparatorPrefix = "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500 ",
            VerticalPipe = "#5a6a7a", SegmentPipe = "#5a6a7a bold",
            NoAgentsText = "#5a6a7a", ConversationNameStyle = "italic #80a0ff",
            UserMessagePrefix = "[#c080a0] Me:[/] ",
        },
        Thinking = new ThinkingStyles
        {
            HeaderStyle = "bold #c0c0c0 on #0a1a2a", TextStyle = "#c0c0c0", MoreStyle = "#5a6a7a",
        },
        Messages = new MessageStyles
        {
            BannerBorder = "#80a0ff", HelpCommand = "#5a6a7a",
            Info = "#5a6a7a", Highlight = "#80a0ff", Emphasis = "bold",
            Success = "#c080a0", Warning = "#eaeaea", Error = "#c080a0",
            RecordingIndicator = "#c080a0", GatewayError = "#c080a0",
            LogTag = "#5a6a7a", LogOk = "#c080a0", LogError = "#c080a0",
            FallbackWarning = "#eaeaea", FallbackFrom = "#c080a0", FallbackTo = "#c080a0",
            ModelFailed = "#c080a0", AgentBadge = "#c0c0c0 on #0a1a2a", IntroductionBorder = "#80a0ff",
            PanelCap = "#5a6a7a", Working = "#eaeaea",
        },
        Panel = new PanelStyles
        {
            Hint = "#5a6a7a", SelectedBg = "#0a1a2a",
            SelectedName = "bold #c0c0c0", Action = "#5a6a7a",
            ActionSelected = "#c0c0c0", Time = "#5a6a7a",
            SectionHeader = "bold #80a0ff",
            ActiveName = "underline", ActiveAgentAction = "underline",
        },
        StreamShell = new StreamShellStyles
        {
            CursorMarkup = "bold #000c18 on #c0c0c0", SelectionMarkup = "bold #c0c0c0 on #0a1a2a",
            CommandSlashMarkup = "#80a0ff", InputPrefixStyle = "bold #80a0ff",
        },
    };
}

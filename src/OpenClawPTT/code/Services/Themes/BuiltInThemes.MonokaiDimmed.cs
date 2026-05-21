namespace OpenClawPTT.Services.Themes;

public static partial class BuiltInThemes
{
    // ── Monokai Dimmed ────────────────────────────────────────────────

    /// <summary>Monokai Dimmed — desaturated, muted version of Monokai.</summary>
    public static ThemeConfig MonokaiDimmed => new()
    {
        Name = "Monokai Dimmed",
        Author = "OpenClaw PTT",
        Markdown = new MarkdownTheme
        {
            CodeContentStyle = "#c0c0c0 on #1e1e1e",
            InlineCodeStyle = "bold #c0c0c0 on #3a3a3a",
            HeadingH1Style = "bold underline #c7444a",
            HeadingH2Style = "bold underline #60805a",
            HeadingH3PlusStyle = "bold #d0874a",
            BlockquoteStyle = "italic #676767",
            BoldItalicStyle = "bold italic #c7444a",
            BoldStyle = "bold #c7444a",
            ItalicStyle = "italic #c0c0c0",
            StrikethroughStyle = "strikethrough #676767",
        },
        Table = new TableTheme { EdgeColor = "#50808a" },
        Tools = MonokaiDimmedTools,
        Palette = new PaletteStyle
        {
            SelectedStyle = "on #3a3a3a",
            SelectedCursorColor = "#c0c0c0",
            SelectedCursorSymbol = "→",
            SelectedNameColor = "#c0c0c0",
            SelectedDescriptionColor = "#676767",
            NormalIndent = " ",
            NormalNameColor = "#50808a",
            NavigationHintStyle = "dim #676767",
        },
    };

    private static readonly ToolTheme MonokaiDimmedTools = new()
    {
        HeaderStyle = "bold #c0c0c0 on #3a3a3a",
        General = new GeneralStyles
        {
            Label = "#c0c0c0", Muted = "#676767", Value = "#c0c0c0",
            Separator = "#676767", MutedSeparator = "#3a3a3a", TruncatedMore = "#676767",
        },
        Kvp = new KvpStyles
        {
            Separator = "#676767", Key = "#676767", Value = "#c0c0c0", Label = "#676767",
        },
        Exec = new ExecStyles
        {
            FileSystem = "#60805a", FileContent = "#50808a", Build = "#c7444a",
            PackageManager = "#c7444a", Network = "#50808a", Scripting = "#b8a85a",
            Process = "#d0874a", HereDoc = "#676767", Vcs = "#d0874a",
            Positional = "#50808a", LongFlag = "#60805a", ShortFlag = "#d0874a",
            EnvKey = "#50808a", EnvValue = "#b8a85a", ScriptBody = "#676767",
            HereDocSummary = "#676767", PathIcon = "#676767", PathText = "#676767",
        },
        Diff = new DiffStyles
        {
            Added = "#c0c0c0 on #2d4a2d", Removed = "#c0c0c0 on #5a3030", Prefix = "#676767",
        },
        Reader = new ReaderStyles
        {
            LineInfo = "#676767", FetchUrl = "#c0c0c0", FetchMaxInfo = "#676767",
        },
        StatusBar = new StatusBarStyles
        {
            SeparatorChar = "\u2500", SeparatorCharMarkup = "#676767",
            TopLeftSeparatorPrefix = "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500 ",
            VerticalPipe = "#676767", SegmentPipe = "#676767 bold",
            NoAgentsText = "#676767", ConversationNameStyle = "italic #50808a",
            UserMessagePrefix = "[#60805a] Me:[/] ",
        },
        Thinking = new ThinkingStyles
        {
            HeaderStyle = "bold #c0c0c0 on #3a3a3a", TextStyle = "#c0c0c0", MoreStyle = "#676767",
        },
        Messages = new MessageStyles
        {
            BannerBorder = "#d0874a", HelpCommand = "#676767",
            Info = "#676767", Highlight = "#50808a", Emphasis = "bold",
            Success = "#60805a", Warning = "#b8a85a", Error = "#c7444a",
            RecordingIndicator = "#c7444a", GatewayError = "#c7444a",
            LogTag = "#676767", LogOk = "#60805a", LogError = "#c7444a",
            FallbackWarning = "#b8a85a", FallbackFrom = "#c7444a", FallbackTo = "#60805a",
            ModelFailed = "#c7444a", AgentBadge = "#c0c0c0 on #3a3a3a", IntroductionBorder = "#d0874a",
            PanelCap = "#676767", Working = "#b8a85a",
        },
        Panel = new PanelStyles
        {
            Hint = "#676767", SelectedBg = "#373737",
            SelectedName = "bold #c0c0c0", Action = "#676767",
            ActionSelected = "#c0c0c0", Time = "#676767",
            SectionHeader = "bold #50808a",
            ActiveName = "underline", ActiveAgentAction = "underline",
        },
        StreamShell = new StreamShellStyles
        {
            CursorMarkup = "bold #1e1e1e on #c0c0c0", SelectionMarkup = "bold #c0c0c0 on #3a3a3a",
            CommandSlashMarkup = "#c7444a", InputPrefixStyle = "bold #50808a",
        },
    };
}

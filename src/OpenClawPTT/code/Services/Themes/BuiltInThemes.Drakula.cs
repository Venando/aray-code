namespace OpenClawPTT.Services.Themes;

public static partial class BuiltInThemes
{
    // ── Drakula ───────────────────────────────────────────────────────

    /// <summary>Dracula-inspired dark theme with purple/magenta accents.</summary>
    public static ThemeConfig Drakula => new()
    {
        Name = "drakula",
        Author = "OpenClaw PTT",
        Markdown = new MarkdownTheme
        {
            CodeFenceStartMarkup = "[#6272a4]\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500[#ff79c6 italic]code[/]\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500[/]",
            CodeFenceEndMarkup = "[#6272a4]\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500[/]",
            CodeContentStyle = "#f8f8f2 on #282a36",
            InlineCodeStyle = "bold #f8f8f2 on #44475a",
            HeadingH1Style = "bold underline #ffb86c",
            HeadingH2Style = "bold underline #bd93f9",
            HeadingH3PlusStyle = "bold #ff79c6",
            BlockquoteStyle = "italic #6272a4",
            ThematicBreakMarkup = "[#6272a4]\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500[/]",
            BoldItalicStyle = "bold italic #ff79c6",
            BoldStyle = "bold #ff79c6",
            ItalicStyle = "italic #f8f8f2",
            StrikethroughStyle = "strikethrough #6272a4",
        },
        Table = new TableTheme { EdgeColor = "#bd93f9" },
        Tools = DrakTools,
        Palette = new PaletteStyle
        {
            SelectedStyle = "on #44475a",
            SelectedCursorColor = "#f8f8f2",
            SelectedCursorSymbol = "→",
            SelectedNameColor = "#f8f8f2",
            SelectedDescriptionColor = "#6272a4",
            NormalIndent = " ",
            NormalNameColor = "#bd93f9",
            NavigationHintStyle = "dim #6272a4",
        },
    };

    private static readonly ToolTheme DrakTools = new()
    {
        HeaderStyle = "bold #f8f8f2 on #44475a",
        General = new GeneralStyles
        {
            Label = "#f8f8f2", Muted = "#6272a4", Value = "#f8f8f2",
            Separator = "#6272a4", MutedSeparator = "#44475a", TruncatedMore = "#6272a4",
        },
        Kvp = new KvpStyles
        {
            Separator = "#6272a4", Key = "#6272a4", Value = "#f8f8f2", Label = "#6272a4",
        },
        Exec = new ExecStyles
        {
            FileSystem = "#50fa7b", FileContent = "#8be9fd", Build = "#ff79c6",
            PackageManager = "#ff5555", Network = "#8be9fd", Scripting = "#f1fa8c",
            Process = "#ffb86c", HereDoc = "#6272a4", Vcs = "#ffb86c",
            Positional = "#8be9fd", LongFlag = "#50fa7b", ShortFlag = "#ffb86c",
            EnvKey = "#8be9fd", EnvValue = "#f1fa8c", ScriptBody = "#6272a4",
            HereDocSummary = "#6272a4", PathIcon = "#6272a4", PathText = "#6272a4",
        },
        Diff = new DiffStyles
        {
            Added = "#f8f8f2 on #3b8055", Removed = "#f8f8f2 on #8b3a3a", Prefix = "#6272a4",
        },
        Reader = new ReaderStyles
        {
            LineInfo = "#6272a4", FetchUrl = "#f8f8f2", FetchMaxInfo = "#6272a4",
        },
        StatusBar = new StatusBarStyles
        {
            SeparatorChar = "\u2500", SeparatorCharMarkup = "#6272a4",
            TopLeftSeparatorPrefix = "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500 ",
            VerticalPipe = "#6272a4", SegmentPipe = "#6272a4 bold",
            NoAgentsText = "#6272a4", ConversationNameStyle = "italic #bd93f9",
            UserMessagePrefix = "[#50fa7b] Me:[/] ",
        },
        Thinking = new ThinkingStyles
        {
            HeaderStyle = "bold #f8f8f2 on #44475a", TextStyle = "#f8f8f2", MoreStyle = "#6272a4",
        },
        Messages = new MessageStyles
        {
            BannerBorder = "#bd93f9", HelpCommand = "#6272a4",
            Info = "#6272a4", Highlight = "#bd93f9", Emphasis = "bold",
            Success = "#50fa7b", Warning = "#ffb86c", Error = "#ff5555",
            RecordingIndicator = "#ff5555", GatewayError = "#ff5555",
            LogTag = "#6272a4", LogOk = "#50fa7b", LogError = "#ff5555",
            FallbackWarning = "#ffb86c", FallbackFrom = "#ff5555", FallbackTo = "#50fa7b",
            ModelFailed = "#ff5555", AgentBadge = "#f8f8f2 on #44475a", IntroductionBorder = "#bd93f9",
            PanelCap = "#6272a4", Working = "#ffb86c",
        },
        Panel = new PanelStyles
        {
            Hint = "#6272a4", SelectedBg = "#44475a",
            SelectedName = "bold #f8f8f2", Action = "#6272a4",
            ActionSelected = "#f8f8f2", Time = "#6272a4",
            SectionHeader = "bold #bd93f9",
            ActiveName = "underline", ActiveAgentAction = "underline",
        },
        StreamShell = new StreamShellStyles
        {
            CursorMarkup = "bold #282a36 on #f8f8f2", SelectionMarkup = "bold #f8f8f2 on #44475a",
            CommandSlashMarkup = "#ff79c6", NativeCommandNameStyle = "#f1fa8c", InputPrefixStyle = "bold #bd93f9",
        },
    };
}

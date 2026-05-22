namespace OpenClawPTT.Services.Themes;

public static partial class BuiltInThemes
{
    // ── Monokai ───────────────────────────────────────────────────────

    /// <summary>Classic Monokai — yellow/pink/orange on dark olive background.</summary>
    public static ThemeConfig Monokai => new()
    {
        Name = "Monokai",
        Author = "OpenClaw PTT",
        Markdown = new MarkdownTheme
        {
            CodeContentStyle = "#f8f8f2 on #272822",
            InlineCodeStyle = "bold #f8f8f2 on #49483e",
            HeadingH1Style = "bold underline #f92672",
            HeadingH2Style = "bold underline #a6e22e",
            HeadingH3PlusStyle = "bold #fd971f",
            BlockquoteStyle = "italic #75715e",
            BoldItalicStyle = "bold italic #f92672",
            BoldStyle = "bold #f92672",
            ItalicStyle = "italic #f8f8f2",
            StrikethroughStyle = "strikethrough #75715e",
        },
        Table = new TableTheme { EdgeColor = "#66d9ef" },
        Tools = MonokaiTools,
        Palette = new PaletteStyle
        {
            SelectedBackground = "on #49483e",
            SelectedCursorColor = "#f8f8f2",
            SelectedCursorSymbol = "→",
            SelectedNameColor = "#f8f8f2",
            SelectedDescriptionColor = "#75715e",
            NormalIndent = " ",
            NormalNameColor = "#66d9ef",
            NavigationHintStyle = "dim #75715e",
        },
    };

    private static readonly ToolTheme MonokaiTools = new()
    {
        HeaderStyle = "bold #f8f8f2 on #49483e",
        General = new GeneralStyles
        {
            Label = "#f8f8f2", Muted = "#75715e", Value = "#f8f8f2",
            Separator = "#75715e", MutedSeparator = "#49483e", TruncatedMore = "#75715e",
        },
        Kvp = new KvpStyles
        {
            Separator = "#75715e", Key = "#75715e", Value = "#f8f8f2", Label = "#75715e",
        },
        Exec = new ExecStyles
        {
            FileSystem = "#a6e22e", FileContent = "#66d9ef", Build = "#f92672",
            PackageManager = "#f92672", Network = "#66d9ef", Scripting = "#e6db74",
            Process = "#fd971f", HereDoc = "#75715e", Vcs = "#fd971f",
            Positional = "#66d9ef", LongFlag = "#a6e22e", ShortFlag = "#fd971f",
            EnvKey = "#66d9ef", EnvValue = "#e6db74", ScriptBody = "#75715e",
            HereDocSummary = "#75715e", PathIcon = "#75715e", PathText = "#75715e",
        },
        Diff = new DiffStyles
        {
            Added = "#f8f8f2 on #3b8055", Removed = "#f8f8f2 on #8b3a3a", Prefix = "#75715e",
        },
        Reader = new ReaderStyles
        {
            LineInfo = "#75715e", FetchUrl = "#f8f8f2", FetchMaxInfo = "#75715e",
        },
        StatusBar = new StatusBarStyles
        {
            SeparatorChar = "\u2500", SeparatorCharMarkup = "#75715e",
            TopLeftSeparatorPrefix = "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500 ",
            VerticalPipe = "#75715e", SegmentPipe = "#75715e bold",
            NoAgentsText = "#75715e", ConversationNameStyle = "italic #66d9ef",
            UserMessagePrefix = "[#a6e22e] Me:[/] ",
        },
        Thinking = new ThinkingStyles
        {
            HeaderStyle = "bold #f8f8f2 on #49483e", TextStyle = "#f8f8f2", MoreStyle = "#75715e",
        },
        Messages = new MessageStyles
        {
            BannerBorder = "#fd971f", HelpCommand = "#75715e",
            Info = "#75715e", Highlight = "#66d9ef", Emphasis = "bold",
            Success = "#a6e22e", Warning = "#e6db74", Error = "#f92672",
            RecordingIndicator = "#f92672", GatewayError = "#f92672",
            LogTag = "#75715e", LogOk = "#a6e22e", LogError = "#f92672",
            FallbackWarning = "#e6db74", FallbackFrom = "#f92672", FallbackTo = "#a6e22e",
            ModelFailed = "#f92672", AgentBadge = "#f8f8f2 on #49483e", IntroductionBorder = "#fd971f",
            PanelCap = "#75715e", Working = "#e6db74",
        },
        Panel = new PanelStyles
        {
            Hint = "#75715e", SelectedBg = "#49483e",
            SelectedName = "bold #f8f8f2", Action = "#75715e",
            ActionSelected = "#f8f8f2", Time = "#75715e",
            SectionHeader = "bold #66d9ef",
            ActiveName = "underline", ActiveAgentAction = "underline",
        },
        StreamShell = new StreamShellStyles
        {
            CursorMarkup = "bold #272822 on #f8f8f2", SelectionMarkup = "bold #f8f8f2 on #49483e",
            CommandSlashMarkup = "#f92672", NativeCommandNameStyle = "#e6db74", InputPrefixStyle = "bold #66d9ef",
        },
    };
}

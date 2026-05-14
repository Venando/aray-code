namespace OpenClawPTT.Services.Themes;

/// <summary>
/// Factory methods for in-code-only themes (no JSON file required).
/// Each theme is registered in <see cref="ThemeService"/> and appears
/// in <c>/theme</c> listings alongside file-based themes.
/// Properties not explicitly set inherit <see cref="ThemeConfig.Default"/> values.
/// </summary>
public static class BuiltInThemes
{
    // ── Drakula ───────────────────────────────────────────────────────

    /// <summary>Dracula-inspired dark theme with purple/magenta accents.</summary>
    public static ThemeConfig Drakula => new()
    {
        Name = "drakula",
        Author = "OpenClaw PTT",
        Markdown = new MarkdownTheme
        {
            CodeFenceStartMarkup = "[#6272a4]\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500[#ff79c6 italic]code[/]\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500[/]",
            CodeFenceEndMarkup = "[#6272a4]\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500[/]",
            CodeContentStyle = "#f8f8f2 on #282a36",
            InlineCodeStyle = "bold #f8f8f2 on #44475a",
            HeadingH1Style = "bold underline #ffb86c",
            HeadingH2Style = "bold underline #bd93f9",
            HeadingH3PlusStyle = "bold #ff79c6",
            BlockquoteStyle = "italic #6272a4",
            ThematicBreakMarkup = "[#6272a4]\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500[/]",
            BoldItalicStyle = "bold italic #ff79c6",
            BoldStyle = "bold #ff79c6",
            ItalicStyle = "italic #f8f8f2",
            StrikethroughStyle = "strikethrough #6272a4",
        },
        Table = new TableTheme { EdgeColor = "#bd93f9" },
        Tools = DrakTools,
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
            Info = "#6272a4", Success = "#50fa7b", Warning = "#ffb86c", Error = "#ff5555",
            RecordingIndicator = "#ff5555", GatewayError = "#ff5555",
            LogTag = "#6272a4", LogOk = "#50fa7b", LogError = "#ff5555",
            FallbackWarning = "#ffb86c", FallbackFrom = "#ff5555", FallbackTo = "#50fa7b",
            ModelFailed = "#ff5555", AgentBadge = "#f8f8f2 on #44475a", IntroductionBorder = "#bd93f9",
            PanelCap = "#6272a4",
        },
        Panel = new PanelStyles
        {
            Hint = "#6272a4", SelectedBg = "#44475a",
            SelectedName = "bold #f8f8f2", Action = "#6272a4",
            ActionSelected = "#f8f8f2", Time = "#6272a4",
        },
        StreamShell = new StreamShellStyles
        {
            CursorMarkup = "bold #282a36 on #f8f8f2", SelectionMarkup = "bold #f8f8f2 on #44475a",
            CommandSlashMarkup = "#ff79c6", InputPrefixStyle = "bold #bd93f9",
        },
    };

    // ── Dark Modern ──────────────────────────────────────────────────

    /// <summary>VS Code Dark Modern — clean blue-on-dark-gray palette.</summary>
    public static ThemeConfig DarkModern => new()
    {
        Name = "Dark Modern",
        Author = "OpenClaw PTT",
        Tools = DarkModernTools,
    };

    private static readonly ToolTheme DarkModernTools = new()
    {
        HeaderStyle = "bold #cccccc on #2d2d2d",
        General = new GeneralStyles
        {
            Label = "#cccccc", Muted = "#808080", Value = "#cccccc",
            Separator = "#808080", MutedSeparator = "#2d2d2d",
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
            EnvKey = "#569cd6", EnvValue = "#dcdcaa",
        },
        Diff = new DiffStyles
        {
            Added = "#cccccc on #1b4d1b", Removed = "#cccccc on #5a1d1d",
        },
        StatusBar = new StatusBarStyles
        {
            SeparatorCharMarkup = "#808080", TopLeftSeparatorPrefix = "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500 ",
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
            Info = "#808080", Success = "#4ec9b0", Warning = "#dcdcaa", Error = "#d16969",
            RecordingIndicator = "#d16969", GatewayError = "#d16969",
            LogTag = "#808080", LogOk = "#4ec9b0", LogError = "#d16969",
            FallbackWarning = "#dcdcaa", FallbackFrom = "#d16969", FallbackTo = "#4ec9b0",
            ModelFailed = "#d16969", AgentBadge = "#cccccc on #2d2d2d", IntroductionBorder = "#569cd6",
            PanelCap = "#808080",
        },
        Panel = new PanelStyles
        {
            Hint = "#808080", SelectedBg = "#3c3c3c",
            SelectedName = "bold #cccccc", Action = "#808080",
            ActionSelected = "#cccccc", Time = "#808080",
        },
        StreamShell = new StreamShellStyles
        {
            CursorMarkup = "bold #1e1e1e on #cccccc", SelectionMarkup = "bold #cccccc on #3c3c3c",
            CommandSlashMarkup = "#c586c0", InputPrefixStyle = "bold #569cd6",
        },
    };

    // ── Dark+ ─────────────────────────────────────────────────────────

    /// <summary>VS Code Dark+ — vibrant version with punchier accents.</summary>
    public static ThemeConfig DarkPlus => new()
    {
        Name = "Dark+",
        Author = "OpenClaw PTT",
        Markdown = new MarkdownTheme
        {
            HeadingH1Style = "bold underline #569cd6",
            HeadingH2Style = "bold underline #4ec9b0",
            HeadingH3PlusStyle = "bold #c586c0",
            InlineCodeStyle = "bold #d4d4d4 on #3c3c3c",
        },
        Table = new TableTheme { EdgeColor = "#569cd6" },
        Tools = DarkPlusTools,
    };

    private static readonly ToolTheme DarkPlusTools = new()
    {
        HeaderStyle = "bold #d4d4d4 on #333333",
        General = new GeneralStyles
        {
            Label = "#d4d4d4", Muted = "#808080", Value = "#d4d4d4",
            Separator = "#808080", MutedSeparator = "#333333",
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
            EnvKey = "#569cd6", EnvValue = "#dcdcaa",
        },
        Diff = new DiffStyles
        {
            Added = "#d4d4d4 on #1b4d1b", Removed = "#d4d4d4 on #5a1d1d",
        },
        StatusBar = new StatusBarStyles
        {
            SeparatorCharMarkup = "#808080",
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
            Info = "#808080", Success = "#4ec9b0", Warning = "#dcdcaa", Error = "#d16969",
            RecordingIndicator = "#d16969", GatewayError = "#d16969",
            LogTag = "#808080", LogOk = "#4ec9b0", LogError = "#d16969",
            FallbackWarning = "#dcdcaa", FallbackFrom = "#d16969", FallbackTo = "#4ec9b0",
            ModelFailed = "#d16969", AgentBadge = "#d4d4d4 on #3c3c3c", IntroductionBorder = "#569cd6",
            PanelCap = "#808080",
        },
        Panel = new PanelStyles
        {
            Hint = "#808080", SelectedBg = "#04395e",
            SelectedName = "bold #d4d4d4", Action = "#808080",
            ActionSelected = "#d4d4d4", Time = "#808080",
        },
        StreamShell = new StreamShellStyles
        {
            CursorMarkup = "bold #1e1e1e on #d4d4d4", SelectionMarkup = "bold #d4d4d4 on #264f78",
            CommandSlashMarkup = "#c586c0", InputPrefixStyle = "bold #569cd6",
        },
    };

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
        },
        Table = new TableTheme { EdgeColor = "#66d9ef" },
        Tools = MonokaiTools,
    };

    private static readonly ToolTheme MonokaiTools = new()
    {
        HeaderStyle = "bold #f8f8f2 on #49483e",
        General = new GeneralStyles
        {
            Label = "#f8f8f2", Muted = "#75715e", Value = "#f8f8f2",
            Separator = "#75715e", MutedSeparator = "#49483e",
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
            EnvKey = "#66d9ef", EnvValue = "#e6db74",
        },
        Diff = new DiffStyles
        {
            Added = "#f8f8f2 on #3b8055", Removed = "#f8f8f2 on #8b3a3a",
        },
        StatusBar = new StatusBarStyles
        {
            SeparatorCharMarkup = "#75715e",
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
            Info = "#75715e", Success = "#a6e22e", Warning = "#e6db74", Error = "#f92672",
            RecordingIndicator = "#f92672", GatewayError = "#f92672",
            LogTag = "#75715e", LogOk = "#a6e22e", LogError = "#f92672",
            FallbackWarning = "#e6db74", FallbackFrom = "#f92672", FallbackTo = "#a6e22e",
            ModelFailed = "#f92672", AgentBadge = "#f8f8f2 on #49483e", IntroductionBorder = "#fd971f",
            PanelCap = "#75715e",
        },
        Panel = new PanelStyles
        {
            Hint = "#75715e", SelectedBg = "#49483e",
            SelectedName = "bold #f8f8f2", Action = "#75715e",
            ActionSelected = "#f8f8f2", Time = "#75715e",
        },
        StreamShell = new StreamShellStyles
        {
            CursorMarkup = "bold #272822 on #f8f8f2", SelectionMarkup = "bold #f8f8f2 on #49483e",
            CommandSlashMarkup = "#f92672", InputPrefixStyle = "bold #66d9ef",
        },
    };

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
        },
        Table = new TableTheme { EdgeColor = "#50808a" },
        Tools = MonokaiDimmedTools,
    };

    private static readonly ToolTheme MonokaiDimmedTools = new()
    {
        HeaderStyle = "bold #c0c0c0 on #3a3a3a",
        General = new GeneralStyles
        {
            Label = "#c0c0c0", Muted = "#676767", Value = "#c0c0c0",
            Separator = "#676767", MutedSeparator = "#3a3a3a",
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
            EnvKey = "#50808a", EnvValue = "#b8a85a",
        },
        Diff = new DiffStyles
        {
            Added = "#c0c0c0 on #2d4a2d", Removed = "#c0c0c0 on #5a3030",
        },
        StatusBar = new StatusBarStyles
        {
            SeparatorCharMarkup = "#676767",
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
            Info = "#676767", Success = "#60805a", Warning = "#b8a85a", Error = "#c7444a",
            RecordingIndicator = "#c7444a", GatewayError = "#c7444a",
            LogTag = "#676767", LogOk = "#60805a", LogError = "#c7444a",
            FallbackWarning = "#b8a85a", FallbackFrom = "#c7444a", FallbackTo = "#60805a",
            ModelFailed = "#c7444a", AgentBadge = "#c0c0c0 on #3a3a3a", IntroductionBorder = "#d0874a",
            PanelCap = "#676767",
        },
        Panel = new PanelStyles
        {
            Hint = "#676767", SelectedBg = "#373737",
            SelectedName = "bold #c0c0c0", Action = "#676767",
            ActionSelected = "#c0c0c0", Time = "#676767",
        },
        StreamShell = new StreamShellStyles
        {
            CursorMarkup = "bold #1e1e1e on #c0c0c0", SelectionMarkup = "bold #c0c0c0 on #3a3a3a",
            CommandSlashMarkup = "#c7444a", InputPrefixStyle = "bold #50808a",
        },
    };

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
        },
        Table = new TableTheme { EdgeColor = "#2aa198" },
        Tools = SolarizedTools,
    };

    private static readonly ToolTheme SolarizedTools = new()
    {
        HeaderStyle = "bold #839496 on #073642",
        General = new GeneralStyles
        {
            Label = "#839496", Muted = "#586e75", Value = "#839496",
            Separator = "#586e75", MutedSeparator = "#073642",
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
            EnvKey = "#268bd2", EnvValue = "#b58900",
        },
        Diff = new DiffStyles
        {
            Added = "#839496 on #1b3b1b", Removed = "#839496 on #5a1d1d",
        },
        StatusBar = new StatusBarStyles
        {
            SeparatorCharMarkup = "#586e75",
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
            Info = "#586e75", Success = "#859900", Warning = "#b58900", Error = "#dc322f",
            RecordingIndicator = "#dc322f", GatewayError = "#dc322f",
            LogTag = "#586e75", LogOk = "#859900", LogError = "#dc322f",
            FallbackWarning = "#b58900", FallbackFrom = "#dc322f", FallbackTo = "#859900",
            ModelFailed = "#dc322f", AgentBadge = "#839496 on #073642", IntroductionBorder = "#268bd2",
            PanelCap = "#586e75",
        },
        Panel = new PanelStyles
        {
            Hint = "#586e75", SelectedBg = "#073642",
            SelectedName = "bold #93a1a1", Action = "#586e75",
            ActionSelected = "#93a1a1", Time = "#586e75",
        },
        StreamShell = new StreamShellStyles
        {
            CursorMarkup = "bold #002b36 on #93a1a1", SelectionMarkup = "bold #93a1a1 on #073642",
            CommandSlashMarkup = "#d33682", InputPrefixStyle = "bold #268bd2",
        },
    };

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
        },
        Table = new TableTheme { EdgeColor = "#80a0ff" },
        Tools = AbyssTools,
    };

    private static readonly ToolTheme AbyssTools = new()
    {
        HeaderStyle = "bold #c0c0c0 on #0a1a2a",
        General = new GeneralStyles
        {
            Label = "#c0c0c0", Muted = "#5a6a7a", Value = "#c0c0c0",
            Separator = "#5a6a7a", MutedSeparator = "#0a1a2a",
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
            EnvKey = "#80a0ff", EnvValue = "#eaeaea",
        },
        Diff = new DiffStyles
        {
            Added = "#c0c0c0 on #1b3b5a", Removed = "#c0c0c0 on #5a2a3a",
        },
        StatusBar = new StatusBarStyles
        {
            SeparatorCharMarkup = "#5a6a7a",
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
            Info = "#5a6a7a", Success = "#c080a0", Warning = "#eaeaea", Error = "#c080a0",
            RecordingIndicator = "#c080a0", GatewayError = "#c080a0",
            LogTag = "#5a6a7a", LogOk = "#c080a0", LogError = "#c080a0",
            FallbackWarning = "#eaeaea", FallbackFrom = "#c080a0", FallbackTo = "#c080a0",
            ModelFailed = "#c080a0", AgentBadge = "#c0c0c0 on #0a1a2a", IntroductionBorder = "#80a0ff",
            PanelCap = "#5a6a7a",
        },
        Panel = new PanelStyles
        {
            Hint = "#5a6a7a", SelectedBg = "#0a1a2a",
            SelectedName = "bold #c0c0c0", Action = "#5a6a7a",
            ActionSelected = "#c0c0c0", Time = "#5a6a7a",
        },
        StreamShell = new StreamShellStyles
        {
            CursorMarkup = "bold #000c18 on #c0c0c0", SelectionMarkup = "bold #c0c0c0 on #0a1a2a",
            CommandSlashMarkup = "#80a0ff", InputPrefixStyle = "bold #80a0ff",
        },
    };
}

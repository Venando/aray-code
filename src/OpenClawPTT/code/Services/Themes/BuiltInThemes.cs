namespace OpenClawPTT.Services.Themes;

/// <summary>
/// Factory methods for in-code-only themes (no JSON file required).
/// Each theme is registered in <see cref="ThemeService"/> and appears
/// in <c>/theme</c> listings alongside file-based themes.
/// </summary>
public static class BuiltInThemes
{
    /// <summary>
    /// Dracula-inspired dark theme with purple/magenta accents.
    /// Uses dark backgrounds and high-contrast foregrounds throughout.
    /// <c>drakula</c> is intentionally misspelled (no 'c').
    /// </summary>
    public static ThemeConfig Drakula => new()
    {
        Name = "drakula",
        Author = "OpenClaw PTT",
        Markdown = new MarkdownTheme
        {
            CodeFenceStartMarkup = "[#6272a4]\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500[#ff79c6 italic]code[/]─────────────────[/]",
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
        Table = new TableTheme
        {
            EdgeColor = "#bd93f9",
        },
        Tools = new ToolTheme
        {
            HeaderStyle = "bold #f8f8f2 on #44475a",

            // General
            Label = "#f8f8f2",
            Muted = "#6272a4",
            Value = "#f8f8f2",
            Separator = "#6272a4",
            MutedSeparator = "#44475a",
            TruncatedMore = "#6272a4",

            // KVP
            KvpSeparator = "#6272a4",
            KvpKey = "#6272a4",
            KvpValue = "#f8f8f2",
            KvpLabel = "#6272a4",

            // Exec command types
            ExecFileSystem = "#50fa7b",
            ExecFileContent = "#8be9fd",
            ExecBuild = "#ff79c6",
            ExecPackageManager = "#ff5555",
            ExecNetwork = "#8be9fd",
            ExecScripting = "#f1fa8c",
            ExecProcess = "#ffb86c",
            ExecHereDoc = "#6272a4",
            ExecVcs = "#ffb86c",

            // Exec structural
            ExecPositional = "#8be9fd",
            ExecLongFlag = "#50fa7b",
            ExecShortFlag = "#ffb86c",
            ExecEnvKey = "#8be9fd",
            ExecEnvValue = "#f1fa8c",
            ExecScriptBody = "#6272a4",
            ExecHereDocSummary = "#6272a4",
            ExecPathIcon = "#6272a4",
            ExecPathText = "#6272a4",

            // Edit / Diff
            DiffAdded = "default on #50fa7b",
            DiffRemoved = "default on #ff5555",
            DiffPrefix = "#6272a4",

            // Read
            ReadLineInfo = "#6272a4",

            // WebFetch
            FetchUrl = "#f8f8f2",
            FetchMaxInfo = "#6272a4",

            // Status bar / separators
            SeparatorChar = "\u2500",
            SeparatorCharMarkup = "#6272a4",
            TopLeftSeparatorPrefix = "\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500 ",
            StatusVerticalPipe = "#6272a4",
            StatusSegmentPipe = "#6272a4 bold",
            StatusNoAgentsText = "#6272a4",
            ConversationNameStyle = "italic #bd93f9",
            UserMessagePrefix = "[#50fa7b] Me:[/] ",

            // StreamShell
            StreamCursorMarkup = "bold #282a36 on #f8f8f2",
            StreamSelectionMarkup = "bold #f8f8f2 on #44475a",
            StreamCommandSlashMarkup = "#ff79c6",
            StreamInputPrefixStyle = "bold #bd93f9",
        },
    };
}

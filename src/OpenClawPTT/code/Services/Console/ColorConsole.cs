using OpenClawPTT.Services.Themes;
using Spectre.Console;

namespace OpenClawPTT.Services;

/// <summary>
/// Default implementation of IColorConsole. Routes all output through
/// a StreamShell host for markup rendering.
/// </summary>
public sealed class ColorConsole : IColorConsole
{
    public const string AppEmoji = "🦞";
    private readonly IStreamShellHost _shellHost;
    private AgentReplyFormatter? _userMessageFormatter;
    private StreamShellCapturingConsole? _userMessageCapturingConsole;

    /// <inheritdoc />
    public LogLevel LogLevel { get; set; } = LogLevel.Error;

    /// <inheritdoc />
    public string UserMessagePrefix
    {
        get => ThemeProvider.Current.Tools.UserMessagePrefix;
        set { } // Kept for interface compat; value comes from active theme
    }

    /// <inheritdoc />
    public int ReservedRightMargin { get; set; } = 10;

    /// <summary>
    /// Creates a new ColorConsole instance with the specified StreamShell host.
    /// Log level defaults to Error (only errors shown). Update <see cref="LogLevel"/> at runtime.
    /// </summary>
    public ColorConsole(IStreamShellHost shellHost)
    {
        _shellHost = shellHost ?? throw new ArgumentNullException(nameof(shellHost));
    }

    /// <inheritdoc />
    public void ApplyConsoleConfig(AppConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));

        // Compute final right-edge margin: max(config indent, 10% of console width)
        ReservedRightMargin = Math.Max(
            config.RightMarginIndent,
            (int)(ConsoleMetrics.GetWindowWidth() * 0.1));

        // Apply StreamShell settings (theme-driven cursor/selection/commandSlash/prefix)
        _shellHost.SetRightMarginIndent(ReservedRightMargin);

        int prefixWidth = Markup.Remove(ThemeProvider.Current.Tools.UserMessagePrefix).Length;
        _shellHost.ApplyStreamShellTheme(prefixWidth);
    }

    private AgentReplyFormatter GetOrCreateUserMessageFormatter()
    {
        if (_userMessageFormatter == null)
        {
            _userMessageCapturingConsole = new StreamShellCapturingConsole(_shellHost);
            _userMessageFormatter = new AgentReplyFormatter("", ReservedRightMargin, prefixAlreadyPrinted: false, output: _userMessageCapturingConsole);
        }
        return _userMessageFormatter;
    }

    private void ShellMsg(string markup) => _shellHost.AddMessage(markup);

    // ── Banner and Help ────────────────────────────────────────

    /// <inheritdoc />
    public void PrintBanner()
    {
        ShellMsg("");
        ShellMsg("[deepskyblue3]  ╔═══════════════════════════════════════╗[/]");
        ShellMsg($"[deepskyblue3]  ║    {AppEmoji}  OpenClaw Push-to-Talk  v1.0    ║[/]");
        ShellMsg("[deepskyblue3]  ╚═══════════════════════════════════════╝[/]");
        ShellMsg("");
    }

    /// <inheritdoc />
    public void PrintHelpMenu(AppConfig appConfig)
    {
        //PrintAgentIntroduction(appConfig);
        ShellMsg("    type [grey]/crew [/]to list agents [grey]/chat <agent>[/] to switch");
        ShellMsg("");
    }

    /// <inheritdoc />
    public void PrintAgentIntroduction(AppConfig appConfig)
    {
        if (!AgentRegistry.IsActiveAgentAvailable)
        {
            return;
        }

        var tableColor = "deepskyblue3";

        var hotkeyCombination = AgentSettingsPersistenceLegacy.GetPersistedHotkey(AgentRegistry.ActiveAgentId!) ?? appConfig.HotkeyCombination;
        AgentRegistry.GetActiveNameAndEmoji(out var agentName, out var emoji);
        var color = AgentRegistry.GetActiveColor();
        var effectiveColor = color ?? AgentPersistedSettings.DefaultColor;
        var nameStr = agentName?.ToString() ?? "";
        var coloredName = $"[{effectiveColor}]{Markup.Escape(nameStr)}[/]";
        var modeDescription = appConfig.HoldToTalk ? "Hold-to-talk" : "Toggle recording";
        var middleContent = $"   Agent: [white on gray15]{emoji} {coloredName}[/] [{tableColor}]·[/] [white on gray15]{Markup.Escape($"[{hotkeyCombination}]")}[/] [{tableColor}]·[/] {modeDescription} [{tableColor}]·[/] /help [{tableColor}]·[/] /quit    ";
        var dashCount = Markup.Remove(middleContent).Length;
        var topLineStart = $"── {AppEmoji} PTT Active ─";
        var topLine = $"[{tableColor}]╭{topLineStart}{new string('─', dashCount - topLineStart.Length)}╮[/]";
        var bottomLine = $"[{tableColor}]╰{new string('─', dashCount)}╯[/]";
        ShellMsg(topLine);
        ShellMsg($"[{tableColor}]│[/]{middleContent}[{tableColor}]│[/]");
        ShellMsg(bottomLine);
        ShellMsg("");
    }

    // ── General Output ─────────────────────────────────────────

    /// <inheritdoc />
    public void PrintMarkup(string markup) => ShellMsg(markup);

    /// <inheritdoc />
    public void PrintUserMessage(string text)
    {
        PrintFormatted(UserMessagePrefix, text);
    }

    public void PrintFormatted(string prefix, string text)
    {
        var fmt = GetOrCreateUserMessageFormatter();
        fmt.Reconfigure(prefix);
        fmt.ProcessDelta(Markup.Escape(text));
        fmt.Finish();
        _userMessageCapturingConsole!.FlushToStreamShell(prefix);
    }

    /// <inheritdoc />
    public void PrintMarkupedUserMessage(string text)
    {
        ShellMsg($"{UserMessagePrefix}{text}");
    }

    // ── Status Messages ────────────────────────────────────────

    /// <inheritdoc />
    public void PrintInfo(string message)
    {
        ShellMsg($"[grey]  {Markup.Escape(message)}[/]");
    }

    /// <inheritdoc />
    public void PrintSuccess(string message)
    {
        ShellMsg($"[green]  ✓ {Markup.Escape(message)}[/]");
    }

    /// <inheritdoc />
    public void PrintSuccessWordWrap(string prefix, string message, int rightMarginIndent)
    {
        ShellMsg($"[green]  ✓ {Markup.Escape(prefix)}{Markup.Escape(message)}[/]");
    }

    /// <inheritdoc />
    public void PrintWarning(string message)
    {
        ShellMsg($"[yellow]  ⚠ {Markup.Escape(message)}[/]");
    }

    /// <inheritdoc />
    public void PrintError(string message)
    {
        ShellMsg($"[red]  ✗ {Markup.Escape(message)}[/]");
    }

    /// <inheritdoc />
    public void PrintRecordingIndicator(bool isRecording, string hotkeyCombination, bool holdToTalk)
    {
        if (!isRecording) return;

        var action = holdToTalk ? $"release {Markup.Escape(hotkeyCombination)}" : $"press {Markup.Escape(hotkeyCombination)} again";
        ShellMsg($"[red]  ● REC — {action} to stop[/]");
    }

    // ── Agent Replies ──────────────────────────────────────────

    /// <inheritdoc />
    public void PrintAgentReply(string prefix, string body)
    {
        ShellMsg($"{prefix}{Markup.Escape(body)}");
    }

    /// <inheritdoc />
    public void PrintAgentReplyWithMarkdown(string prefix, string body)
    {
        ShellMsg($"{prefix}{body}");
    }

    /// <inheritdoc />
    public void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix)
    {
        ShellMsg(Markup.Escape(delta));
    }

    /// <inheritdoc />
    public void PrintAgentReplyDelta(string prefix, string delta, string newlineSuffix, AppConfig config)
    {
        PrintAgentReplyDelta(prefix, delta, newlineSuffix);
    }

    // ── Gateway Errors ─────────────────────────────────────────

    /// <inheritdoc />
    public void PrintGatewayError(string message, string? detailCode = null, string? recommendedStep = null)
    {
        ShellMsg($"[red]  Gateway error: {Markup.Escape(message)}[/]");
        if (detailCode != null)
            ShellMsg($"  Detail code : {Markup.Escape(detailCode)}");
        if (recommendedStep != null)
            ShellMsg($"  Recommended : {Markup.Escape(recommendedStep)}");
    }

    /// <inheritdoc />
    public void PrintModelFallback(string fromProvider, string fromModel, string toProvider, string toModel, bool isQuotaError)
    {
        if (isQuotaError)
        {
            ShellMsg($"[orange1]  ⚠ Model fallback: [red]{Markup.Escape(fromProvider)}/{Markup.Escape(fromModel)}[/] quota exhausted → [green]{Markup.Escape(toProvider)}/{Markup.Escape(toModel)}[/][/]");
            ShellMsg($"[orange1]  ⚠ Tip: Recharge [bold]{Markup.Escape(fromProvider)}[/] API quota or switch primary model in config[/]");
        }
        else
        {
            ShellMsg($"[yellow]  ⚠ Model fallback: [red]{Markup.Escape(fromProvider)}/{Markup.Escape(fromModel)}[/] unavailable → [green]{Markup.Escape(toProvider)}/{Markup.Escape(toModel)}[/][/]");
        }

        ShellMsg("");
    }

    /// <inheritdoc />
    public void PrintModelFailed(string errorMessage)
    {
        ShellMsg($"[red]  ✗ Model failed: {Markup.Escape(errorMessage)}[/]");
        ShellMsg("");
    }

    // ── Logging ────────────────────────────────────────────────

    /// <inheritdoc />
    public void Log(string tag, string msg, LogLevel level = LogLevel.Debug)
    {
        if (level > LogLevel) return;
        ShellMsg($"[grey]  {Markup.Escape($"[{tag}]")} {Markup.Escape(msg)}[/]");
    }

    /// <inheritdoc />
    public void LogOk(string tag, string msg, LogLevel level = LogLevel.Info)
    {
        if (level > LogLevel) return;
        ShellMsg($"[green]  {Markup.Escape($"[{tag}]")} {Markup.Escape(msg)}[/]");
    }

    /// <inheritdoc />
    public void LogError(string tag, string msg)
    {
        if (LogLevel == LogLevel.None) return;
        ShellMsg($"[red]  {Markup.Escape($"[{tag}]")} {Markup.Escape(msg)}[/]");
    }

    // ── StreamShell Access ─────────────────────────────────────

    /// <inheritdoc />
    public IStreamShellHost? GetStreamShellHost() => _shellHost;
}

using System;
using ArayCode.Formatting;
using ArayCode.Services.Themes;
using Spectre.Console;
using StreamShell;

namespace ArayCode.Services;

/// <summary>
/// Default implementation of IColorConsole. Routes all output through
/// a StreamShell host for markup rendering.
/// All Spectre styles driven from <see cref="ThemeProvider.Current.Tools"/>.
/// </summary>
public sealed class ColorConsole : IColorConsole
{
    private readonly IStreamShellHost _shellHost;
    private AgentReplyFormatter? _userMessageFormatter;
    private StreamShellCapturingConsole? _userMessageCapturingConsole;
    private const string AppEmoji = "⚡";

    public ColorConsole(IStreamShellHost shellHost)
    {
        _shellHost = shellHost ?? throw new ArgumentNullException(nameof(shellHost));
    }

    /// <inheritdoc />
    public LogLevel LogLevel { get; set; } = LogLevel.Error;

    /// <inheritdoc />
    public int ReservedRightMargin { get; set; } = 10;

    /// <inheritdoc />
    public string UserMessagePrefix
    {
        get => ThemeProvider.Current.Tools.StatusBar.UserMessagePrefix;
        set { }
    }

    /// <inheritdoc />
    public void ApplyConsoleConfig(AppConfig config)
    {
        if (config == null) throw new ArgumentNullException(nameof(config));

        ReservedRightMargin = Math.Max(
            config.RightMarginIndent,
            (int)(ConsoleMetrics.GetWindowWidth() * 0.1));

        _shellHost.SetRightMarginIndent(ReservedRightMargin);

        int prefixWidth = Markup.Remove(ThemeProvider.Current.Tools.StatusBar.UserMessagePrefix).Length;
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

    private static ToolTheme T => ThemeProvider.Current.Tools;

    // ── Banner and Help ────────────────────────────────────────

    /// <inheritdoc />
    public void PrintBanner()
    {
        ShellMsg("");
        ShellMsg($"[{T.Messages.BannerBorder}]  ╔═══════════════════════════════════════╗[/]");
        ShellMsg($"[{T.Messages.BannerBorder}]  ║    {AppEmoji}  OpenClaw Push-to-Talk  v1.0    ║[/]");
        ShellMsg($"[{T.Messages.BannerBorder}]  ╚═══════════════════════════════════════╝[/]");
        ShellMsg("");
    }

    /// <inheritdoc />
    public void PrintHelpMenu(AppConfig appConfig)
    {
        ShellMsg($"    type [{T.Messages.HelpCommand}]/crew [/]to list agents [{T.Messages.HelpCommand}]/chat <agent>[/] to switch");
        ShellMsg("");
    }

    /// <inheritdoc />
    public void PrintAgentIntroduction(AppConfig appConfig)
    {
        if (!AgentRegistry.IsActiveAgentAvailable)
            return;

        var borderStyle = T.Messages.IntroductionBorder;
        var badgeStyle = T.Messages.AgentBadge;

        var hotkeyCombination = AgentSettingsPersistenceLegacy.GetPersistedHotkey(AgentRegistry.ActiveAgentId!) ?? appConfig.HotkeyCombination;
        AgentRegistry.GetActiveNameAndEmoji(out var agentName, out var emoji);
        var color = AgentRegistry.GetActiveColor();
        var effectiveColor = color ?? AgentPersistedSettings.DefaultColor;
        var nameStr = agentName?.ToString() ?? "";
        var coloredName = $"[{effectiveColor}]{Markup.Escape(nameStr)}[/]";
        var modeDescription = appConfig.HoldToTalk ? "Hold-to-talk" : "Toggle recording";
        var middleContent = $"   Agent: [{badgeStyle}]{emoji} {coloredName}[/] [{borderStyle}]·[/] [{badgeStyle}]{Markup.Escape($"[{hotkeyCombination}]")}[/] [{borderStyle}]·[/] {modeDescription} [{borderStyle}]·[/] /apphelp    ";
        var dashCount = Markup.Remove(middleContent).Length;
        var topLineStart = $"\u2500\u2500 {AppEmoji} PTT Active \u2500";
        var topLine = $"[{borderStyle}]\u256d{topLineStart}{new string('\u2500', dashCount - topLineStart.Length - 1)}\u256e[/]";
        var bottomLine = $"[{borderStyle}]\u2570{new string('\u2500', dashCount)}\u256f[/]";
        ShellMsg(topLine);
        ShellMsg($"[{borderStyle}]\u2502[/]{middleContent}[{borderStyle}]\u2502[/]");
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
        ShellMsg($"[{T.Messages.Info}]  {Markup.Escape(message)}[/]");
    }

    /// <inheritdoc />
    public void PrintSuccess(string message)
    {
        ShellMsg($"[{T.Messages.Success}]  \u2713 {Markup.Escape(message)}[/]");
    }

    /// <inheritdoc />
    public void PrintSuccessWordWrap(string prefix, string message, int rightMarginIndent)
    {
        ShellMsg($"[{T.Messages.Success}]  \u2713 {Markup.Escape(prefix)}{Markup.Escape(message)}[/]");
    }

    /// <inheritdoc />
    public void PrintWarning(string message)
    {
        ShellMsg($"[{T.Messages.Warning}]  \u26a0 {Markup.Escape(message)}[/]");
    }

    /// <inheritdoc />
    public void PrintError(string message)
    {
        ShellMsg($"[{T.Messages.Error}]  \u2717 {Markup.Escape(message)}[/]");
    }

    /// <inheritdoc />
    public void PrintRecordingIndicator(bool isRecording, string hotkeyCombination, bool holdToTalk)
    {
        if (!isRecording) return;

        var action = holdToTalk ? $"release {Markup.Escape(hotkeyCombination)}" : $"press {Markup.Escape(hotkeyCombination)} again";
        ShellMsg($"[{T.Messages.RecordingIndicator}]  \u25cf REC \u2014 {action} to stop[/]");
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
        ShellMsg($"[{T.Messages.GatewayError}]  Gateway error: {Markup.Escape(message)}[/]");
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
            ShellMsg($"[{T.Messages.FallbackWarning}]  \u26a0 Model fallback: [{T.Messages.FallbackFrom}]{Markup.Escape(fromProvider)}/{Markup.Escape(fromModel)}[/] quota exhausted \u2192 [{T.Messages.FallbackTo}]{Markup.Escape(toProvider)}/{Markup.Escape(toModel)}[/][/]");
            ShellMsg($"[{T.Messages.FallbackWarning}]  \u26a0 Tip: Recharge [{T.Messages.Emphasis}]{Markup.Escape(fromProvider)}[/] API quota or switch primary model in config[/]");
        }
        else
        {
            ShellMsg($"[{T.Messages.Warning}]  \u26a0 Model fallback: [{T.Messages.FallbackFrom}]{Markup.Escape(fromProvider)}/{Markup.Escape(fromModel)}[/] unavailable \u2192 [{T.Messages.FallbackTo}]{Markup.Escape(toProvider)}/{Markup.Escape(toModel)}[/][/]");
        }

        ShellMsg("");
    }

    /// <inheritdoc />
    public void PrintModelFailed(string errorMessage)
    {
        ShellMsg($"[{T.Messages.ModelFailed}]  \u2717 Model failed: {Markup.Escape(errorMessage)}[/]");
        ShellMsg("");
    }

    // ── Logging ────────────────────────────────────────────────

    /// <inheritdoc />
    public void Log(string tag, string msg, LogLevel level = LogLevel.Debug)
    {
        if (level > LogLevel) return;
        ShellMsg($"[{T.Messages.LogTag}]  {Markup.Escape($"[{tag}]")} {Markup.Escape(msg)}[/]");
    }

    /// <inheritdoc />
    public void LogOk(string tag, string msg, LogLevel level = LogLevel.Info)
    {
        if (level > LogLevel) return;
        ShellMsg($"[{T.Messages.LogOk}]  {Markup.Escape($"[{tag}]")} {Markup.Escape(msg)}[/]");
    }

    /// <inheritdoc />
    public void LogError(string tag, string msg)
    {
        if (LogLevel == LogLevel.None) return;
        ShellMsg($"[{T.Messages.LogError}]  {Markup.Escape($"[{tag}]")} {Markup.Escape(msg)}[/]");
    }

    // ── StreamShell Access ─────────────────────────────────────

    /// <inheritdoc />
    public IStreamShellHost? GetStreamShellHost() => _shellHost;
}

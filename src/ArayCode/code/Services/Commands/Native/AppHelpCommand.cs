using ArayCode.Services.Themes;
using Spectre.Console;

namespace ArayCode.Services.Commands;

/// <summary>
/// Native command: /apphelp — prints all PTT client native commands with
/// descriptions, grouped by category. Also points users to OpenClaw gateway
/// commands (accessed via /help when connected).
/// </summary>
public sealed class AppHelpCommand : ICommand
{
    private readonly IStreamShellHost _host;
    private readonly IColorConsole _console;

    public string Name => "apphelp";
    public string Description => "Show all native PTT commands and OpenClaw command reference";
    public CommandSource Source => CommandSource.Native;
    public ShellCommandType Type => ShellCommandType.Discovery;
    public string[]? Suggestions => null;

    public AppHelpCommand(IStreamShellHost host, IColorConsole console)
    {
        _host = host;
        _console = console;
    }

    public Task ExecuteAsync(string[] args, Dictionary<string, string> namedArgs, CancellationToken ct = default)
    {
        var T = ThemeProvider.Current.Tools;
        var muted = T.General.Muted;
        var highlight = T.Messages.Highlight;
        var emphasis = T.Messages.Emphasis;
        var info = T.Messages.Info;
        var warning = T.Messages.Warning;
        var success = T.Messages.Success;

        _host.AddMessage("");
        _host.AddMessage($"  [{highlight}]══════ Native PTT Commands ══════[/]");
        _host.AddMessage($"  [{muted}]Commands built into the client. Some require a gateway connection.[/]");
        _host.AddMessage("");

        // ── System ──────────────────────────────────────────────────────────
        PrintGroup("System", new[]
        {
            ("/quit", "Exit the application"),
            ("/clean", "Clear the terminal screen"),
        });

        // ── Agent Management ────────────────────────────────────────────────
        PrintGroup("Agent Management", new[]
        {
            ("/crew", "List available agents. \"/crew config\" for setup"),
            ("/chat", "<name|id> Switch active agent by name or ID"),
        });

        // ── Configuration ─────────────────────────────────────────────────────
        PrintGroup("Configuration", new[]
        {
            ("/reconfigure", "Run reconfiguration wizard"),
            ("/appconfig", "<key> [value] Get or set any app config value"),
            ("/theme", "Show current theme or \"/theme <name>\" to switch"),
        });

        // ── Session & History ─────────────────────────────────────────────
        PrintGroup("Session & History", new[]
        {
            ("/history", "[[N]] Load N session history entries (needs GW)"),
        });

        // ── Gateway ─────────────────────────────────────────────────────────
        PrintGroup("Gateway", new[]
        {
            ("/reconnect", "Reconnect to the gateway"),
        });

        // ── Diagnostics ─────────────────────────────────────────────────────
        PrintGroup("Diagnostics", new[]
        {
            ("/appstatus", "Show detailed GW/TTS/STT/LLM status panel"),
            ("/errors", "[N] Show recent gateway errors. \"/errors clear\" to wipe"),
        });

        // ── Direct LLM ──────────────────────────────────────────────────────
        PrintGroup("Direct LLM", new[]
        {
            ("/llm", "<message|summary-test|title-test> Send to configured LLM"),
        });

        _host.AddMessage($"  [{highlight}]══════ OpenClaw Gateway Commands ══════[/]");
        _host.AddMessage($"  [{muted}]Forwarded to the OpenClaw gateway. Only available when connected.[/]");
        _host.AddMessage($"  [{muted}]Use /help for the full list, or /commands for a categorized catalog.[/]");

        _host.AddMessage("");
        _host.AddMessage($"  [{info}]Tip:[/] [{muted}]Type [/][{emphasis}]/apphelp[/][{muted}] anytime to see this again.[/]");
        _host.AddMessage("");

        return Task.CompletedTask;
    }

    private void PrintGroup(string groupName, (string cmd, string desc)[] commands)
    {
        var muted = ThemeProvider.Current.Tools.General.Muted;
        var emphasis = ThemeProvider.Current.Tools.Messages.Emphasis;

        _host.AddMessage($"  [{muted}]{groupName}[/]");
        foreach (var (cmd, desc) in commands)
        {
            _host.AddMessage($"    [{emphasis}]{Markup.Escape(cmd)}[/]  {Markup.Escape(desc)}");
        }
        _host.AddMessage("");
    }

    private void PrintOpenClawQuickRef((string cmd, string desc)[] commands)
    {
        var muted = ThemeProvider.Current.Tools.General.Muted;
        var emphasis = ThemeProvider.Current.Tools.Messages.Emphasis;

        foreach (var (cmd, desc) in commands)
        {
            _host.AddMessage($"    [{emphasis}]{Markup.Escape(cmd)}[/]  {Markup.Escape(desc)}");
        }
    }
}

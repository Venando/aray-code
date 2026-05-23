using System.Collections.Frozen;

namespace ArayCode.Services.Commands;

/// <summary>
/// Metadata for all known OpenClaw slash commands: descriptions, type classifications,
/// and autocomplete suggestions. Replaces the static <see cref="OpenClawCommands"/> class
/// with a richer, type-aware registry.
/// </summary>
public static class OpenClawCommandMetadata
{
    // ── Descriptions ─────────────────────────────────────────────────────

    public static readonly IReadOnlyDictionary<string, string> Descriptions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Sessions and runs
            ["new"] = " [model] — starts a new session (alias: /reset)",
            ["reset"] = " [soft [message]] — resets or soft-resets the current session",
            ["compact"] = " [instructions] — compacts session context",
            ["stop"] = " — aborts the current run",
            ["session"] = " idle|max-age — manage thread-binding expiry",
            ["export-session"] = " [path] — exports current session to HTML",
            ["export"] = " [path] — exports current session to HTML",
            ["export-trajectory"] = " [path] — exports JSONL trajectory bundle",
            ["trajectory"] = " [path] — exports JSONL trajectory bundle",

            // Model directives
            ["think"] = " <level> — sets thinking level",
            ["thinking"] = " <level> — alias for /think",
            ["t"] = " <level> — alias for /think",
            ["verbose"] = " on|off|full — toggles verbose tool output",
            ["v"] = " on|off|full — alias for /verbose",
            ["trace"] = " on|off — toggles plugin trace output",
            ["fast"] = " [status|on|off] — shows or sets fast mode",
            ["reasoning"] = " [on|off|stream] — toggles reasoning visibility",
            ["reason"] = " [on|off|stream] — alias for /reasoning",
            ["elevated"] = " [on|off|ask|full] — toggles elevated mode",
            ["elev"] = " [on|off|ask|full] — alias for /elevated",
            ["exec"] = " host=... security=... — shows or sets exec defaults",
            ["model"] = " [name|#|status] — shows or sets the active model",
            ["models"] = " [provider] [page] — lists providers or models",
            ["queue"] = " <mode> — manages queue behavior",

            // Discovery
            ["help"] = " — shows short help summary",
            ["commands"] = " — shows the generated command catalog",
            ["tools"] = " [compact|verbose] — shows available tools",
            ["status"] = " — shows execution/runtime status",
            ["diagnostics"] = " [note] — support-report flow",
            ["tasks"] = " — lists active/recent background tasks",
            ["context"] = " [list|detail|json] — explains context assembly",
            ["whoami"] = " — shows your sender ID",
            ["id"] = " — shows your sender ID",
            ["usage"] = " off|tokens|full|cost — usage footer control",
            ["crestodian"] = " [request] — Crestodian setup helper",

            // Skills / approvals
            ["skill"] = " <name> [input] — runs a skill by name",
            ["allowlist"] = " [list|add|remove] — manage allowlist entries",
            ["approve"] = " <id> <decision> — resolves exec approval prompts",

            // Subagents / ACP
            ["subagents"] = " list|kill|log|info|send|steer|spawn — manage sub-agents",
            ["acp"] = " spawn|cancel|steer|close|sessions|status — ACP sessions",
            ["focus"] = " <target> — binds thread to session target",
            ["unfocus"] = " — removes thread binding",
            ["agents"] = " — lists thread-bound agents",
            ["kill"] = " <id|#|all> — aborts running sub-agents",
            ["steer"] = " <id|#> <message> — sends steering to sub-agent",
            ["tell"] = " <id|#> <message> — alias for /steer",

            // Admin
            ["config"] = " show|get|set|unset — gateway config (owner-only)",
            ["mcp"] = " show|get|set|unset — MCP config (owner-only)",
            ["plugins"] = " list|inspect|install|enable|disable — plugin management",
            ["plugin"] = " — alias for /plugins",
            ["debug"] = " show|set|unset|reset — runtime overrides (owner-only)",
            ["restart"] = " — restarts OpenClaw gateway",
            ["send"] = " on|off|inherit — sets send policy (owner-only)",

            // Voice / TTS
            ["tts"] = " on|off|status|chat|latest|provider|limit|summary|audio|help",
            ["activation"] = " mention|always — sets group activation mode",
            ["bash"] = " <command> — runs a host shell command",
            ["voice"] = " status|list|set — manages Talk voice config",

            // Bundled utilities
            ["btw"] = " <question> — side question without context change",
            ["dreaming"] = " [on|off|status|help] — toggles memory dreaming",
            ["pair"] = " [qr|status|pending|approve|cleanup|notify] — device pairing",
            ["phone"] = " status|arm|disarm — high-risk phone node commands",
            ["codex"] = " status|models|threads|resume|compact|review|diagnostics — Codex harness",
            ["card"] = " ... — sends LINE rich card presets",

            // Discord native
            ["vc"] = " join|leave|status — Discord voice channels",

            // Docking
            ["dock-discord"] = " — switch reply route to Discord",
            ["dock_discord"] = " — alias for /dock-discord",
            ["dock-mattermost"] = " — switch reply route to Mattermost",
            ["dock_mattermost"] = " — alias for /dock-mattermost",
            ["dock-slack"] = " — switch reply route to Slack",
            ["dock_slack"] = " — alias for /dock-slack",
            ["dock-telegram"] = " — switch reply route to Telegram",
            ["dock_telegram"] = " — alias for /dock-telegram",
        };

    // ── Type classifications ─────────────────────────────────────────────

    public static readonly IReadOnlyDictionary<string, ShellCommandType> ShellCommandTypes =
        new Dictionary<string, ShellCommandType>(StringComparer.OrdinalIgnoreCase)
        {
            // Session control
            ["new"] = ShellCommandType.SessionControl,
            ["reset"] = ShellCommandType.SessionControl,
            ["compact"] = ShellCommandType.SessionControl,
            ["stop"] = ShellCommandType.SessionControl,
            ["session"] = ShellCommandType.SessionControl,
            ["export-session"] = ShellCommandType.SessionControl,
            ["export"] = ShellCommandType.SessionControl,
            ["export-trajectory"] = ShellCommandType.SessionControl,
            ["trajectory"] = ShellCommandType.SessionControl,

            // Model directives
            ["think"] = ShellCommandType.ModelDirective,
            ["thinking"] = ShellCommandType.ModelDirective,
            ["t"] = ShellCommandType.ModelDirective,
            ["verbose"] = ShellCommandType.ModelDirective,
            ["v"] = ShellCommandType.ModelDirective,
            ["trace"] = ShellCommandType.ModelDirective,
            ["fast"] = ShellCommandType.ModelDirective,
            ["reasoning"] = ShellCommandType.ModelDirective,
            ["reason"] = ShellCommandType.ModelDirective,
            ["elevated"] = ShellCommandType.ModelDirective,
            ["elev"] = ShellCommandType.ModelDirective,
            ["exec"] = ShellCommandType.ModelDirective,
            ["model"] = ShellCommandType.ModelDirective,
            ["models"] = ShellCommandType.ModelDirective,
            ["queue"] = ShellCommandType.ModelDirective,

            // Discovery
            ["help"] = ShellCommandType.Discovery,
            ["commands"] = ShellCommandType.Discovery,
            ["tools"] = ShellCommandType.Discovery,
            ["status"] = ShellCommandType.Discovery,
            ["diagnostics"] = ShellCommandType.Discovery,
            ["tasks"] = ShellCommandType.Discovery,
            ["context"] = ShellCommandType.Discovery,
            ["whoami"] = ShellCommandType.Discovery,
            ["id"] = ShellCommandType.Discovery,
            ["usage"] = ShellCommandType.Discovery,
            ["crestodian"] = ShellCommandType.Discovery,

            // Skills / approvals
            ["skill"] = ShellCommandType.Skill,
            ["allowlist"] = ShellCommandType.Admin,
            ["approve"] = ShellCommandType.Admin,

            // Subagents
            ["subagents"] = ShellCommandType.Subagent,
            ["acp"] = ShellCommandType.Subagent,
            ["focus"] = ShellCommandType.Subagent,
            ["unfocus"] = ShellCommandType.Subagent,
            ["agents"] = ShellCommandType.Subagent,
            ["kill"] = ShellCommandType.Subagent,
            ["steer"] = ShellCommandType.Subagent,
            ["tell"] = ShellCommandType.Subagent,

            // Admin
            ["config"] = ShellCommandType.Admin,
            ["mcp"] = ShellCommandType.Admin,
            ["plugins"] = ShellCommandType.Admin,
            ["plugin"] = ShellCommandType.Admin,
            ["debug"] = ShellCommandType.Admin,
            ["restart"] = ShellCommandType.Admin,
            ["send"] = ShellCommandType.Admin,

            // Voice
            ["tts"] = ShellCommandType.Voice,
            ["activation"] = ShellCommandType.Voice,
            ["bash"] = ShellCommandType.Voice,
            ["voice"] = ShellCommandType.Voice,

            // Utility
            ["btw"] = ShellCommandType.Utility,
            ["dreaming"] = ShellCommandType.Utility,
            ["pair"] = ShellCommandType.Utility,
            ["phone"] = ShellCommandType.Utility,
            ["codex"] = ShellCommandType.Utility,
            ["card"] = ShellCommandType.Utility,

            // Docking
            ["dock-discord"] = ShellCommandType.Dock,
            ["dock_discord"] = ShellCommandType.Dock,
            ["dock-mattermost"] = ShellCommandType.Dock,
            ["dock_mattermost"] = ShellCommandType.Dock,
            ["dock-slack"] = ShellCommandType.Dock,
            ["dock_slack"] = ShellCommandType.Dock,
            ["dock-telegram"] = ShellCommandType.Dock,
            ["dock_telegram"] = ShellCommandType.Dock,

            // Discord native
            ["vc"] = ShellCommandType.Voice,
        };

    /// <summary>All known OpenClaw slash command names (without leading /).</summary>
    public static readonly IReadOnlySet<string> Names =
        new HashSet<string>(Descriptions.Keys, StringComparer.OrdinalIgnoreCase);

    /// <summary>Look up a command description. Returns null if unknown.</summary>
    public static string? GetDescription(string commandName) =>
        Descriptions.TryGetValue(commandName, out var desc) ? desc : null;

    /// <summary>Look up a command type. Returns <see cref="ShellCommandType.Unknown"/> if unmapped.</summary>
    public static ShellCommandType GetShellCommandType(string commandName) =>
        ShellCommandTypes.TryGetValue(commandName, out var type) ? type : ShellCommandType.Unknown;
}

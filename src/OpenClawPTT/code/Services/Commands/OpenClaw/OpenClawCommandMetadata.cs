using System.Collections.Frozen;

namespace OpenClawPTT.Services.Commands;

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

    public static readonly IReadOnlyDictionary<string, CommandType> CommandTypes =
        new Dictionary<string, CommandType>(StringComparer.OrdinalIgnoreCase)
        {
            // Session control
            ["new"] = CommandType.SessionControl,
            ["reset"] = CommandType.SessionControl,
            ["compact"] = CommandType.SessionControl,
            ["stop"] = CommandType.SessionControl,
            ["session"] = CommandType.SessionControl,
            ["export-session"] = CommandType.SessionControl,
            ["export"] = CommandType.SessionControl,
            ["export-trajectory"] = CommandType.SessionControl,
            ["trajectory"] = CommandType.SessionControl,

            // Model directives
            ["think"] = CommandType.ModelDirective,
            ["thinking"] = CommandType.ModelDirective,
            ["t"] = CommandType.ModelDirective,
            ["verbose"] = CommandType.ModelDirective,
            ["v"] = CommandType.ModelDirective,
            ["trace"] = CommandType.ModelDirective,
            ["fast"] = CommandType.ModelDirective,
            ["reasoning"] = CommandType.ModelDirective,
            ["reason"] = CommandType.ModelDirective,
            ["elevated"] = CommandType.ModelDirective,
            ["elev"] = CommandType.ModelDirective,
            ["exec"] = CommandType.ModelDirective,
            ["model"] = CommandType.ModelDirective,
            ["models"] = CommandType.ModelDirective,
            ["queue"] = CommandType.ModelDirective,

            // Discovery
            ["help"] = CommandType.Discovery,
            ["commands"] = CommandType.Discovery,
            ["tools"] = CommandType.Discovery,
            ["status"] = CommandType.Discovery,
            ["diagnostics"] = CommandType.Discovery,
            ["tasks"] = CommandType.Discovery,
            ["context"] = CommandType.Discovery,
            ["whoami"] = CommandType.Discovery,
            ["id"] = CommandType.Discovery,
            ["usage"] = CommandType.Discovery,
            ["crestodian"] = CommandType.Discovery,

            // Skills / approvals
            ["skill"] = CommandType.Skill,
            ["allowlist"] = CommandType.Admin,
            ["approve"] = CommandType.Admin,

            // Subagents
            ["subagents"] = CommandType.Subagent,
            ["acp"] = CommandType.Subagent,
            ["focus"] = CommandType.Subagent,
            ["unfocus"] = CommandType.Subagent,
            ["agents"] = CommandType.Subagent,
            ["kill"] = CommandType.Subagent,
            ["steer"] = CommandType.Subagent,
            ["tell"] = CommandType.Subagent,

            // Admin
            ["config"] = CommandType.Admin,
            ["mcp"] = CommandType.Admin,
            ["plugins"] = CommandType.Admin,
            ["plugin"] = CommandType.Admin,
            ["debug"] = CommandType.Admin,
            ["restart"] = CommandType.Admin,
            ["send"] = CommandType.Admin,

            // Voice
            ["tts"] = CommandType.Voice,
            ["activation"] = CommandType.Voice,
            ["bash"] = CommandType.Voice,
            ["voice"] = CommandType.Voice,

            // Utility
            ["btw"] = CommandType.Utility,
            ["dreaming"] = CommandType.Utility,
            ["pair"] = CommandType.Utility,
            ["phone"] = CommandType.Utility,
            ["codex"] = CommandType.Utility,
            ["card"] = CommandType.Utility,

            // Docking
            ["dock-discord"] = CommandType.Dock,
            ["dock_discord"] = CommandType.Dock,
            ["dock-mattermost"] = CommandType.Dock,
            ["dock_mattermost"] = CommandType.Dock,
            ["dock-slack"] = CommandType.Dock,
            ["dock_slack"] = CommandType.Dock,
            ["dock-telegram"] = CommandType.Dock,
            ["dock_telegram"] = CommandType.Dock,

            // Discord native
            ["vc"] = CommandType.Voice,
        };

    /// <summary>All known OpenClaw slash command names (without leading /).</summary>
    public static readonly IReadOnlySet<string> Names =
        new HashSet<string>(Descriptions.Keys, StringComparer.OrdinalIgnoreCase);

    /// <summary>Look up a command description. Returns null if unknown.</summary>
    public static string? GetDescription(string commandName) =>
        Descriptions.TryGetValue(commandName, out var desc) ? desc : null;

    /// <summary>Look up a command type. Returns <see cref="CommandType.Unknown"/> if unmapped.</summary>
    public static CommandType GetCommandType(string commandName) =>
        CommandTypes.TryGetValue(commandName, out var type) ? type : CommandType.Unknown;
}

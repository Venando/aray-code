using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenClawPTT;

/// <summary>
/// Provides argument autocomplete suggestions for OpenClaw slash commands
/// registered in StreamShell. Returns null for commands without suggestions.
/// </summary>
public static class OpenClawCommandSuggestions
{
    /// <summary>
    /// Common config paths for the /config command (show/get/set/unset).
    /// </summary>
    private static readonly string[] ConfigSuggestions =
    [
        // Subcommands alone
        "show",
        "get",
        "set",
        "unset",

        // Agents defaults
        "show agents.defaults.model",
        "set agents.defaults.model ",
        "show agents.defaults.workspace",
        "set agents.defaults.workspace ",
        "show agents.defaults.thinking",
        "set agents.defaults.thinking ",
        "show agents.defaults.heartbeat.every",
        "set agents.defaults.heartbeat.every ",
        "show agents.defaults.skills",
        "set agents.defaults.skills ",
        "show agents.defaults.sandbox",
        "set agents.defaults.sandbox ",

        // Models
        "show models.mode",
        "set models.mode ",
        "show models.pricing.enabled",
        "set models.pricing.enabled ",

        // Channels
        "show channels",
        "set channels ",

        // Gateway
        "show gateway",
        "set gateway ",

        // Plugins
        "show plugins",
        "set plugins ",

        // Skills
        "show skills",
        "set skills ",

        // MCP
        "show mcp",
        "set mcp ",

        // Browser
        "show browser",
        "set browser ",

        // Session
        "show session",
        "set session ",

        // Messages
        "show messages",
        "set messages ",

        // Talk
        "show talk",
        "set talk ",

        // Auth
        "show auth",
        "set auth ",

        // Commitments
        "show commitments",
        "set commitments ",

        // Logging
        "show logging",
        "set logging ",

        // Diagnostics
        "show diagnostics",
        "set diagnostics ",

        // Update
        "show update",
        "set update ",

        // ACP
        "show acp",
        "set acp ",

        // Cron
        "show cron",
        "set cron ",

        // UI
        "show ui",
        "set ui ",

        // Wizard
        "show wizard",
        "set wizard ",
    ];

    private static readonly Dictionary<string, string[]?> SuggestionsMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["config"] = ConfigSuggestions,
    };

    /// <summary>
    /// Gets argument suggestions for a command, or null if none available.
    /// </summary>
    public static string[]? Get(string commandName) =>
        SuggestionsMap.TryGetValue(commandName, out var suggestions) ? suggestions : null;
}

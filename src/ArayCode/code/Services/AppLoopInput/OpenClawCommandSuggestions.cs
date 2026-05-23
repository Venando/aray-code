using System;
using System.Collections.Generic;

namespace ArayCode;

/// <summary>
/// Provides argument autocomplete suggestions for OpenClaw slash commands
/// registered in StreamShell. Returns null for commands without suggestions.
/// </summary>
public static class OpenClawCommandSuggestions
{
    // ── Common choice sets ─────────────────────────────────────────────────

    private static readonly string[] OnOff = ["on", "off"];
    private static readonly string[] OnOffStatus = ["on", "off", "status"];
    private static readonly string[] OnOffFull = ["on", "off", "full"];
    private static readonly string[] OnOffStream = ["on", "off", "stream"];
    private static readonly string[] OnOffAskFull = ["on", "off", "ask", "full"];
    private static readonly string[] ShowGetSetUnset =
    [
        "show ",
        "get ",
        "set ",
        "unset ",
    ];

    private static readonly string[] ThinkingLevels =
    [
        "off", "minimal", "low", "medium", "high", "xhigh", "adaptive", "max"
    ];

    // ── Config paths ───────────────────────────────────────────────────────

    private static readonly string[] ConfigSuggestions =
    [
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
        // ── Directives with simple toggles ─────────────────────────────────
        ["think"] = ThinkingLevels,
        ["thinking"] = ThinkingLevels,
        ["t"] = ThinkingLevels,
        ["verbose"] = OnOffFull,
        ["v"] = OnOffFull,
        ["trace"] = OnOff,
        ["fast"] = OnOffStatus,
        ["reasoning"] = OnOffStream,
        ["reason"] = OnOffStream,
        ["elevated"] = OnOffAskFull,
        ["elev"] = OnOffAskFull,
        ["send"] = OnOffStatus,
        ["usage"] = ["off", "tokens", "full", "cost"],
        ["session"] = ["idle", "max-age"],

        // ── Discovery / status ─────────────────────────────────────────────
        ["tools"] = ["compact", "verbose"],
        ["context"] = ["list", "detail", "json"],

        // ── Subagent / ACP ─────────────────────────────────────────────────
        ["subagents"] = ["list", "kill", "log", "info", "send", "steer", "spawn"],
        ["acp"] = ["spawn", "cancel", "steer", "close", "sessions", "status"],
        ["kill"] = ["all"],

        // ── Owner admin ────────────────────────────────────────────────────
        ["config"] = ConfigSuggestions,
        ["mcp"] = ShowGetSetUnset,
        ["plugins"] = ["list", "inspect", "show", "get", "install", "enable", "disable"],
        ["plugin"] = ["list", "inspect", "show", "get", "install", "enable", "disable"],
        ["debug"] = ["show", "set", "unset", "reset"],
        ["allowlist"] = ["list", "add", "remove"],

        // ── TTS / voice ────────────────────────────────────────────────────
        ["tts"] = ["on", "off", "status", "chat", "latest", "provider", "limit", "summary", "audio", "help"],
        ["activation"] = ["mention", "always"],
        ["voice"] = ["status", "list", "set"],

        // ── Plugin / bundled ───────────────────────────────────────────────
        ["dreaming"] = OnOffStatus,
        ["pair"] = ["qr", "status", "pending", "approve", "cleanup", "notify"],
        ["phone"] = ["status", "arm", "disarm"],
        ["codex"] = ["status", "models", "threads", "resume", "compact", "review", "diagnostics", "account", "mcp", "skills"],
        ["vc"] = ["join", "leave", "status"],
    };

    /// <summary>
    /// Gets argument suggestions for a command, or null if none available.
    /// </summary>
    public static string[]? Get(string commandName) =>
        SuggestionsMap.TryGetValue(commandName, out var suggestions) ? suggestions : null;

    /// <summary>
    /// Known value suggestions for string-backed AppConfig properties that have
    /// a constrained set of valid values (e.g. API type names).
    /// These are appended to the property-name suggestions so that tab-completion
    /// offers the valid values after the user has typed the key name.
    /// </summary>
    private static readonly Dictionary<string, string[]> KnownConfigPropertyValues = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(AppConfig.DirectLlmApiType)] = new[] { "openai-completions", "anthropic-messages" },
    };

    /// <summary>
    /// Dynamically builds suggestions from all writable public properties on AppConfig.
    /// Used for the local /appconfig command tab completion.
    /// Also appends known valid values for constrained-string properties (e.g. DirectLlmApiType)
    /// so the user can tab-complete the value after typing the key.
    /// </summary>
    public static string[] GetAppConfigSuggestions()
    {
        var propertyNames = typeof(AppConfig)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.CanWrite && p.SetMethod?.IsPublic == true)
            .Select(p => p.Name)
            .ToList();

        // Append known values for constrained-string properties
        foreach (var (propName, values) in KnownConfigPropertyValues)
        {
            if (propertyNames.Contains(propName))
                propertyNames.AddRange(values.Where(v => !propertyNames.Contains(v)));
        }

        return propertyNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

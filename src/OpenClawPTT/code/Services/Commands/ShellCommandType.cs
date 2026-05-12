namespace OpenClawPTT.Services.Commands;

/// <summary>
/// Classifies commands by their functional domain.
/// Applies to both native and OpenClaw commands.
/// </summary>
public enum ShellCommandType
{
    /// <summary>System-level commands: quit, clean screen.</summary>
    System,

    /// <summary>Configuration commands: reconfigure, appconfig, gateway config.</summary>
    Configuration,

    /// <summary>Agent management: crew, chat, agents list.</summary>
    AgentManagement,

    /// <summary>Session lifecycle: new, reset, stop, compact, export, trajectory.</summary>
    SessionControl,

    /// <summary>History display and management.</summary>
    History,

    /// <summary>Gateway connectivity: reconnect, status checks.</summary>
    GatewayControl,

    /// <summary>Diagnostics and error reporting.</summary>
    Diagnostics,

    /// <summary>Direct LLM communication: llm, tts-test.</summary>
    DirectLlm,

    /// <summary>Model and run controls: think, verbose, trace, model, fast, reasoning, elevated, exec, queue.</summary>
    ModelDirective,

    /// <summary>Discovery and introspection: help, commands, tools, status, context, whoami, usage, tasks.</summary>
    Discovery,

    /// <summary>Skill invocation.</summary>
    Skill,

    /// <summary>Subagent and ACP management.</summary>
    Subagent,

    /// <summary>Owner admin and plugin management: config, plugins, debug, restart, allowlist, approve, mcp.</summary>
    Admin,

    /// <summary>Voice, TTS, and audio controls: tts, voice, activation, bash.</summary>
    Voice,

    /// <summary>Utility and bundled plugin commands: btw, dreaming, pair, phone, codex, card.</summary>
    Utility,

    /// <summary>Surface docking commands: dock-discord, dock-slack, etc.</summary>
    Dock,

    /// <summary>Unclassified or unknown command type.</summary>
    Unknown
}

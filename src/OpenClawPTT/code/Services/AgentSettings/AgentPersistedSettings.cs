namespace OpenClawPTT;

/// <summary>Per-agent settings persisted to agents.json.</summary>
public sealed class AgentPersistedSettings
{
    /// <summary>Default color used when no per-agent color override is set.</summary>
    public const string DefaultColor = "cyan";

    public string AgentId { get; set; } = "";
    /// <summary>Hotkey override, e.g. "Alt+1". Null means "use global from config.json".</summary>
    public string? HotkeyCombination { get; set; }
    /// <summary>Emoji override for agent prefix and listings. Null means use default (🤖).</summary>
    public string? Emoji { get; set; }
    /// <summary>Color override for agent name display. Null means use default.</summary>
    public string? Color { get; set; }
    /// <summary>Whether this agent appears in the status bottom panel. Defaults to true.</summary>
    public bool ShowInStatusPanel { get; set; } = true;
}
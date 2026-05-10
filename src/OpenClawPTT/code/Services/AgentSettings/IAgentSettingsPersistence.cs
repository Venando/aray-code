using System;
using System.Collections.Generic;

namespace OpenClawPTT;

/// <summary>
/// Interface for managing per-agent persisted settings (hotkey, emoji, color) persisted to agents.json.
/// </summary>
public interface IAgentSettingsPersistence
{
    /// <summary>Set or clear per-agent hotkey override. Fires PersistedSettingsChanged.</summary>
    void SetPersistedHotkey(string agentId, string? hotkeyCombo);

    /// <summary>Get per-agent hotkey override, or null for global default.</summary>
    string? GetPersistedHotkey(string agentId);

    /// <summary>Set or clear per-agent emoji override. Fires PersistedSettingsChanged.</summary>
    void SetPersistedEmoji(string agentId, string? emoji);

    /// <summary>Get per-agent emoji override, or null for default (🤖).</summary>
    string? GetPersistedEmoji(string agentId);

    /// <summary>Set or clear per-agent color override. Fires PersistedSettingsChanged.</summary>
    void SetPersistedColor(string agentId, string? color);

    /// <summary>Get per-agent color override, or null for default.</summary>
    string? GetPersistedColor(string agentId);

    /// <summary>Get per-agent ShowInStatusPanel flag. Returns true if not explicitly set.</summary>
    bool GetPersistedShowInStatusPanel(string agentId);

    /// <summary>Set or clear per-agent ShowInStatusPanel flag. Fires PersistedSettingsChanged.</summary>
    void SetPersistedShowInStatusPanel(string agentId, bool show);

    /// <summary>All agents with their effective hotkey (override or null).</summary>
    IReadOnlyList<(AgentInfo Agent, string? Hotkey)> AllAgentsWithHotkeys { get; }

    /// <summary>All agents with their effective hotkey, emoji, color, and ShowInStatusPanel.</summary>
    IReadOnlyList<(AgentInfo Agent, string? Hotkey, string? Emoji, string? Color, bool ShowInStatusPanel)> AllAgentSettings { get; }

    /// <summary>True if at least one agent has any persisted setting (hotkey, emoji, or color).</summary>
    bool HasAnyPersistedSettings { get; }

    /// <summary>Merge persisted settings from agents.json into the registry.</summary>
    void MergePersistedSettings(AgentsConfig persisted);

    /// <summary>Fired when persisted settings change via SetPersistedHotkey/SetPersistedEmoji.</summary>
    event Action? PersistedSettingsChanged;
}
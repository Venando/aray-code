using System;
using System.Collections.Generic;

namespace OpenClawPTT;

/// <summary>
/// Static bridge pattern for callers that can't easily use DI (ConsoleUi, AgentRegistry).
/// Delegates to an injected IAgentSettingsPersistence instance.
/// </summary>
public static class AgentSettingsPersistenceLegacy
{
    private static IAgentSettingsPersistence? _instance;
    private static readonly object _lock = new();

    /// <summary>
    /// Initialize the static bridge with an IAgentSettingsPersistence instance.
    /// Called once during application startup (in ServiceFactory or AppBootstrapper).
    /// </summary>
    public static void Initialize(IAgentSettingsPersistence instance)
    {
        lock (_lock)
        {
            _instance = instance ?? throw new ArgumentNullException(nameof(instance));
        }
    }

    private static IAgentSettingsPersistence GetInstance()
    {
        lock (_lock)
        {
            return _instance ?? throw new InvalidOperationException(
                "AgentSettingsPersistenceLegacy not initialized. Call Initialize() during application startup.");
        }
    }

    /// <summary>Get per-agent hotkey override, or null for global default.</summary>
    public static string? GetPersistedHotkey(string agentId) => GetInstance().GetPersistedHotkey(agentId);

    /// <summary>Set or clear per-agent hotkey override.</summary>
    public static void SetPersistedHotkey(string agentId, string? hotkeyCombo) => GetInstance().SetPersistedHotkey(agentId, hotkeyCombo);

    /// <summary>Get per-agent emoji override, or null for default (🤖).</summary>
    public static string? GetPersistedEmoji(string agentId) => GetInstance().GetPersistedEmoji(agentId);

    /// <summary>Set or clear per-agent emoji override.</summary>
    public static void SetPersistedEmoji(string agentId, string? emoji) => GetInstance().SetPersistedEmoji(agentId, emoji);

    /// <summary>Get per-agent color override, or null for default.</summary>
    public static string? GetPersistedColor(string agentId) => GetInstance().GetPersistedColor(agentId);

    /// <summary>Set or clear per-agent color override.</summary>
    public static void SetPersistedColor(string agentId, string? color) => GetInstance().SetPersistedColor(agentId, color);

    /// <summary>All agents with their effective hotkey (override or null).</summary>
    public static IReadOnlyList<(AgentInfo Agent, string? Hotkey)> AllAgentsWithHotkeys => GetInstance().AllAgentsWithHotkeys;

    /// <summary>Get per-agent ShowInStatusPanel flag. Returns true if not explicitly set.</summary>
    public static bool GetPersistedShowInStatusPanel(string agentId) => GetInstance().GetPersistedShowInStatusPanel(agentId);

    /// <summary>Set or clear per-agent ShowInStatusPanel flag.</summary>
    public static void SetPersistedShowInStatusPanel(string agentId, bool show) => GetInstance().SetPersistedShowInStatusPanel(agentId, show);

    /// <summary>All agents with their effective hotkey, emoji, color, and ShowInStatusPanel.</summary>
    public static IReadOnlyList<(AgentInfo Agent, string? Hotkey, string? Emoji, string? Color, bool ShowInStatusPanel)> AllAgentSettings => GetInstance().AllAgentSettings;

    /// <summary>Merge persisted settings from agents.json into the registry.</summary>
    public static void MergePersistedSettings(AgentsConfig persisted) => GetInstance().MergePersistedSettings(persisted);

    /// <summary>Fired when persisted settings change via SetPersistedHotkey/SetPersistedEmoji.</summary>
    public static event Action? PersistedSettingsChanged
    {
        add
        {
            var instance = GetInstance();
            instance.PersistedSettingsChanged += value;
        }
        remove
        {
            // Handle potential null if uninitialized during disposal
            IAgentSettingsPersistence? instance;
            lock (_lock)
            {
                instance = _instance;
            }
            instance?.PersistedSettingsChanged -= value;
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;

namespace ArayCode;

/// <summary>
/// Manages per-agent persisted settings (hotkey, emoji, color) persisted to agents.json.
/// This is the non-static DI-based implementation.
/// </summary>
public sealed class AgentSettingsPersistence : IAgentSettingsPersistence
{
    private readonly object _lock = new();
    private readonly Dictionary<string, AgentPersistedSettings> _agentSettings = new(StringComparer.OrdinalIgnoreCase);
    private readonly AgentSettingsService _settingsService;

    public AgentSettingsPersistence(AgentSettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        // Load existing settings from the service into the local cache
        MergePersistedSettings(settingsService.ToConfig());
    }

    /// <inheritdoc />
    public string? GetPersistedHotkey(string agentId)
    {
        lock (_lock)
        {
            return _agentSettings.TryGetValue(agentId, out var s) ? s.HotkeyCombination : null;
        }
    }

    /// <inheritdoc />
    public void SetPersistedHotkey(string agentId, string? hotkeyCombo)
    {
        var existingEmoji = GetPersistedEmoji(agentId);
        var existingColor = GetPersistedColor(agentId);
        SetPersistedField(agentId, hotkeyCombo, existingEmoji, existingColor);
    }

    /// <inheritdoc />
    public string? GetPersistedEmoji(string agentId)
    {
        lock (_lock)
        {
            return _agentSettings.TryGetValue(agentId, out var s) ? s.Emoji : null;
        }
    }

    /// <inheritdoc />
    public void SetPersistedEmoji(string agentId, string? emoji)
    {
        var existingHotkey = GetPersistedHotkey(agentId);
        var existingColor = GetPersistedColor(agentId);
        SetPersistedField(agentId, existingHotkey, emoji, existingColor);
    }

    /// <inheritdoc />
    public string? GetPersistedColor(string agentId)
    {
        lock (_lock)
        {
            return _agentSettings.TryGetValue(agentId, out var s) ? s.Color : null;
        }
    }

    /// <inheritdoc />
    public void SetPersistedColor(string agentId, string? color)
    {
        var existingHotkey = GetPersistedHotkey(agentId);
        var existingEmoji = GetPersistedEmoji(agentId);
        SetPersistedField(agentId, existingHotkey, existingEmoji, color);
    }

    /// <inheritdoc />
    public IReadOnlyList<(AgentInfo Agent, string? Hotkey)> AllAgentsWithHotkeys
    {
        get
        {
            lock (_lock)
            {
                return AgentRegistry.Agents.Select(a =>
                    (a, _agentSettings.TryGetValue(a.AgentId, out var s) ? s.HotkeyCombination : (string?)null)
                ).ToList().AsReadOnly();
            }
        }
    }

    /// <inheritdoc />
    public bool GetPersistedShowInStatusPanel(string agentId)
    {
        lock (_lock)
        {
            return _agentSettings.TryGetValue(agentId, out var s) ? s.ShowInStatusPanel : true;
        }
    }

    /// <inheritdoc />
    public void SetPersistedShowInStatusPanel(string agentId, bool show)
    {
        lock (_lock)
        {
            if (!_agentSettings.TryGetValue(agentId, out var s))
            {
                s = new AgentPersistedSettings { AgentId = agentId };
                _agentSettings[agentId] = s;
            }
            s.ShowInStatusPanel = show;

            _settingsService.SetShowInStatusPanel(agentId, show);
            _settingsService.Save();
        }
        PersistedSettingsChanged?.Invoke();
    }

    /// <inheritdoc />
    public IReadOnlyList<(AgentInfo Agent, string? Hotkey, string? Emoji, string? Color, bool ShowInStatusPanel)> AllAgentSettings
    {
        get
        {
            lock (_lock)
            {
                return AgentRegistry.Agents.Select(a =>
                {
                    _agentSettings.TryGetValue(a.AgentId, out var s);
                    return (a, s?.HotkeyCombination, s?.Emoji, s?.Color, s?.ShowInStatusPanel ?? true);
                }).ToList().AsReadOnly();
            }
        }
    }

    /// <inheritdoc />
    public bool HasAnyPersistedSettings
    {
        get
        {
            lock (_lock) return _agentSettings.Count > 0;
        }
    }

    /// <inheritdoc />
    public void MergePersistedSettings(AgentsConfig persisted)
    {
        lock (_lock)
        {
            foreach (var s in persisted.Agents)
            {
                _agentSettings[s.AgentId] = s;
            }
        }
    }

    /// <inheritdoc />
    public event Action? PersistedSettingsChanged;

    private void SetPersistedField(string agentId, string? hotkeyCombo, string? emoji, string? color)
    {
        lock (_lock)
        {
            if (!_agentSettings.TryGetValue(agentId, out var s))
            {
                s = new AgentPersistedSettings { AgentId = agentId };
                _agentSettings[agentId] = s;
            }
            s.HotkeyCombination = hotkeyCombo;
            s.Emoji = emoji;
            s.Color = color;

            // Sync with settings service for persistence
            _settingsService.SetHotkey(agentId, hotkeyCombo);
            _settingsService.SetEmoji(agentId, emoji);
            _settingsService.SetColor(agentId, color);
            _settingsService.Save();
        }
        PersistedSettingsChanged?.Invoke();
    }
}
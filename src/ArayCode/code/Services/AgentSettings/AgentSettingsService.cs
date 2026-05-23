using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ArayCode.Services;



namespace ArayCode;

/// <summary>Loads, saves, and queries per-agent settings from agents.json.</summary>
public sealed class AgentSettingsService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly IColorConsole _console;
    private List<AgentPersistedSettings> _settings = new();

    public AgentSettingsService(string dataDir, IColorConsole console)
    {
        var filename = "agents.json";
#if DEBUG
        filename = "agents.debug.json";
#endif
        _filePath = Path.Combine(dataDir, filename);
        _console = console;
    }

    public void Load()
    {
        if (!File.Exists(_filePath))
        {
            _settings = new List<AgentPersistedSettings>();
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var config = JsonSerializer.Deserialize<AgentsConfig>(json, JsonOpts);
            _settings = config?.Agents ?? new List<AgentPersistedSettings>();
        }
        catch (JsonException ex)
        {
            _console.LogError("settings", $"Failed to parse {Path.GetFileName(_filePath)}: {ex.Message}. Starting with empty settings.");
            _settings = new List<AgentPersistedSettings>();
        }
        catch (IOException ex)
        {
            _console.LogError("settings", $"Failed to read {Path.GetFileName(_filePath)}: {ex.Message}. Starting with empty settings.");
            _settings = new List<AgentPersistedSettings>();
        }
    }

    public void Save()
    {
        try
        {
            var config = new AgentsConfig { Agents = _settings };
            var dir = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(config, JsonOpts));
            _console.LogOk("settings", $"Saved {_settings.Count} agent(s) to {_filePath}");
        }
        catch (Exception ex)
        {
            _console.LogError("settings", $"Failed to save agents file: {ex.Message}");
        }
    }

    public string? GetHotkey(string agentId)
    {
        var entry = _settings.FirstOrDefault(s =>
            s.AgentId.Equals(agentId, System.StringComparison.OrdinalIgnoreCase));
        return entry?.HotkeyCombination;
    }

    public void SetHotkey(string agentId, string? hotkeyCombo)
        => SetField(agentId, s => s.HotkeyCombination = hotkeyCombo);

    public string? GetEmoji(string agentId)
    {
        var entry = _settings.FirstOrDefault(s =>
            s.AgentId.Equals(agentId, System.StringComparison.OrdinalIgnoreCase));
        return entry?.Emoji;
    }

    public void SetEmoji(string agentId, string? emoji)
        => SetField(agentId, s => s.Emoji = emoji);

    public string? GetColor(string agentId)
    {
        var entry = _settings.FirstOrDefault(s =>
            s.AgentId.Equals(agentId, System.StringComparison.OrdinalIgnoreCase));
        return entry?.Color;
    }

    public void SetColor(string agentId, string? color)
        => SetField(agentId, s => s.Color = color);

    public bool GetShowInStatusPanel(string agentId)
    {
        var entry = _settings.FirstOrDefault(s =>
            s.AgentId.Equals(agentId, System.StringComparison.OrdinalIgnoreCase));
        return entry?.ShowInStatusPanel ?? true;
    }

    public void SetShowInStatusPanel(string agentId, bool show)
        => SetField(agentId, s => s.ShowInStatusPanel = show);

    private void SetField(string agentId, System.Action<AgentPersistedSettings> setter)
    {
        var entry = _settings.FirstOrDefault(s =>
            s.AgentId.Equals(agentId, System.StringComparison.OrdinalIgnoreCase));
        if (entry != null)
        {
            setter(entry);
        }
        else
        {
            entry = new AgentPersistedSettings { AgentId = agentId };
            setter(entry);
            _settings.Add(entry);
        }
    }

    public AgentsConfig ToConfig() => new AgentsConfig { Agents = new List<AgentPersistedSettings>(_settings) };
}
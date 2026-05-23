using System;
using System.Collections.Generic;
using System.IO;
using Moq;
using Xunit;
using ArayCode.Services;

namespace ArayCode.Tests.AgentSettings;

[Collection("AgentRegistryCollection")]
public class AgentRegistrySettingsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AgentSettingsService _settingsService;
    private readonly AgentSettingsPersistence _persistence;
    private bool _disposed;

    public AgentRegistrySettingsTests()
    {
        // Reset between tests
        AgentRegistry.SetAgents(new List<AgentInfo>());

        // Setup persistence with a temp directory
        _tempDir = Path.Combine(Path.GetTempPath(), "AgentRegistrySettingsTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var consoleMock = new Mock<IColorConsole>();
        _settingsService = new AgentSettingsService(_tempDir, consoleMock.Object);
        _persistence = new AgentSettingsPersistence(_settingsService);
    }

    [Fact]
    public void GetPersistedHotkey_NoSettings_ReturnsNull()
    {
        Assert.Null(_persistence.GetPersistedHotkey("agent-1"));
    }

    [Fact]
    public void SetPersistedHotkey_ReturnsValue()
    {
        _persistence.SetPersistedHotkey("agent-1", "Alt+1");
        Assert.Equal("Alt+1", _persistence.GetPersistedHotkey("agent-1"));
    }

    [Fact]
    public void SetPersistedHotkey_NullClearsOverride()
    {
        _persistence.SetPersistedHotkey("agent-1", "Alt+1");
        _persistence.SetPersistedHotkey("agent-1", null);
        Assert.Null(_persistence.GetPersistedHotkey("agent-1"));
    }

    [Fact]
    public void AllAgentsWithHotkeys_ReturnsList()
    {
        var agents = new List<AgentInfo>
        {
            new() { AgentId = "a1", Name = "Alpha", SessionKey = "agent:a1:main", IsDefault = true },
            new() { AgentId = "a2", Name = "Beta", SessionKey = "agent:a2:main" },
        };
        AgentRegistry.SetAgents(agents);
        _persistence.SetPersistedHotkey("a1", "Alt+1");

        var result = _persistence.AllAgentsWithHotkeys;
        Assert.Equal(2, result.Count);
        Assert.Equal("Alt+1", result[0].Hotkey);
    }

    [Fact]
    public void MergePersistedSettings_OverlaysHotkeys()
    {
        var agents = new List<AgentInfo>
        {
            new() { AgentId = "a1", Name = "Alpha", SessionKey = "agent:a1:main", IsDefault = true },
        };
        AgentRegistry.SetAgents(agents);

        var persisted = new AgentsConfig
        {
            Agents = new List<AgentPersistedSettings>
            {
                new() { AgentId = "a1", HotkeyCombination = "Ctrl+Shift+A" }
            }
        };
        _persistence.MergePersistedSettings(persisted);

        Assert.Equal("Ctrl+Shift+A", _persistence.GetPersistedHotkey("a1"));
    }

    [Fact]
    public void SetPersistedHotkey_FiresPersistedSettingsChanged()
    {
        bool fired = false;
        _persistence.PersistedSettingsChanged += () => fired = true;
        _persistence.SetPersistedHotkey("a1", "Alt+1");
        Assert.True(fired);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}

[CollectionDefinition("AgentSettingsRegistry", DisableParallelization = true)]
public class AgentSettingsRegistryCollection
{
}

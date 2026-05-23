using System.IO;
using System;
using Xunit;
using ArayCode.Services;
using Moq;

namespace ArayCode.Tests.AgentSettings;

public class AgentSettingsServiceTests
{
    private static AgentSettingsService CreateService(string dir)
    {
        Directory.CreateDirectory(dir);
        var mockConsole = new Mock<IColorConsole>().Object;
        return new AgentSettingsService(dir, mockConsole);
    }

    [Fact]
    public void Load_FileDoesNotExist_ReturnsEmpty()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var svc = CreateService(dir);
        svc.Load();
        Assert.Null(svc.GetHotkey("agent-1"));
    }

    [Fact]
    public void SetAndGetHotkey_RoundTrips()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var svc = CreateService(dir);
        svc.Load();
        svc.SetHotkey("agent-1", "Alt+1");
        Assert.Equal("Alt+1", svc.GetHotkey("agent-1"));
    }

    [Fact]
    public void SetHotkey_NullClearsOverride()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var svc = CreateService(dir);
        svc.Load();
        svc.SetHotkey("agent-1", "Alt+1");
        svc.SetHotkey("agent-1", null);
        Assert.Null(svc.GetHotkey("agent-1"));
    }

    [Fact]
    public void Save_And_Load_PersistsHotkeys()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var svc1 = CreateService(dir);
        svc1.Load();
        svc1.SetHotkey("agent-a", "Ctrl+Shift+A");
        svc1.Save();

        var svc2 = CreateService(dir);
        svc2.Load();
        Assert.Equal("Ctrl+Shift+A", svc2.GetHotkey("agent-a"));
    }

    [Fact]
    public void ToConfig_ReturnsAllSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var svc = CreateService(dir);
        svc.Load();
        svc.SetHotkey("a1", "Alt+1");
        svc.SetHotkey("a2", "Alt+2");
        var config = svc.ToConfig();
        Assert.Equal(2, config.Agents.Count);
    }
}

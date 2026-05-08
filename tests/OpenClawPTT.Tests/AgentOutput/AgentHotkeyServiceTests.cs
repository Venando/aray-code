using System;
using System.Collections.Generic;
using Moq;
using OpenClawPTT.Services;
using Xunit;

namespace OpenClawPTT.Tests.AgentOutput;

[Collection("AgentHotkeyService")]
public class AgentHotkeyServiceTests : IDisposable
{
    private sealed class NoOpHotkeyHook : IGlobalHotkeyHook
    {
        public event Action? HotkeyPressed { add { } remove { } }
        public event Action? HotkeyReleased { add { } remove { } }
        public event Action<int>? HotkeyIndexPressed { add { } remove { } }
        public event Action<int>? HotkeyIndexReleased { add { } remove { } }
        public event Action? EscapePressed { add { } remove { } }
        public bool BlockEscape { get; set; }
        public void SetHotkey(Hotkey mapping) { }
        public void SetHotkeys(System.Collections.Generic.IEnumerable<Hotkey> hotkeys) { }
        public void Start() { }
        public void Dispose() { }
    }

    private sealed class NoOpHotkeyHookFactory : IHotkeyHookFactory
    {
        public IGlobalHotkeyHook Create(Hotkey mapping, IColorConsole console) => new NoOpHotkeyHook();
    }

    private readonly NoOpHotkeyHookFactory _factory = new();

    private static IAgentSettingsPersistence CreatePersistenceMock()
    {
        var mock = new Mock<IAgentSettingsPersistence>();
        mock.Setup(x => x.AllAgentsWithHotkeys).Returns(new List<(AgentInfo Agent, string? Hotkey)>().AsReadOnly());
        mock.Setup(x => x.AllAgentSettings).Returns(new List<(AgentInfo Agent, string? Hotkey, string? Emoji, string? Color)>().AsReadOnly());
        return mock.Object;
    }

    public AgentHotkeyServiceTests()
    {
        AgentRegistry.SetAgents(new List<AgentInfo>());
    }

    public void Dispose()
    {
        AgentRegistry.SetAgents(new List<AgentInfo>());
    }

    private static AgentInfo MakeAgent(string id, string name = "", bool isDefault = false)
    {
        return new AgentInfo
        {
            AgentId = id,
            Name = string.IsNullOrEmpty(name) ? id : name,
            SessionKey = $"agent:{id}:main",
            IsDefault = isDefault
        };
    }

    [Fact]
    public void HotkeyPressed_ActiveAgent_StartsRecording()
    {
        var agents = new List<AgentInfo> { MakeAgent("a1", "Alpha", isDefault: true) };
        AgentRegistry.SetAgents(agents);

        var pttCtrl = new Mock<IPttController>();
        var mockClient = new Mock<IGatewayClient>();
        var config = new AppConfig();
        var service = new TestableGatewayService(config, mockClient.Object);

        using var svc = new AgentHotkeyService(pttCtrl.Object, Mock.Of<ITextMessageSender>(), Mock.Of<IStreamShellHost>(), config, CreatePersistenceMock(), service, null, _factory);

        svc.HandleHotkeyPressed(0); // a1 is active by default

        pttCtrl.Verify(p => p.StartRecording(), Times.Once);
    }

    [Fact]
    public void HotkeyPressed_InactiveAgent_SwitchesAndShowsMessage()
    {
        var agents = new List<AgentInfo>
        {
            MakeAgent("a1", "Alpha", isDefault: true),
            MakeAgent("a2", "Beta"),
        };
        AgentRegistry.SetAgents(agents);

        var pttCtrl = new Mock<IPttController>();
        var sender = new Mock<ITextMessageSender>();
        var shellHost = new Mock<IStreamShellHost>();
        var mockClient = new Mock<IGatewayClient>();
        var config = new AppConfig();
        var service = new TestableGatewayService(config, mockClient.Object);
        using var svc = new AgentHotkeyService(pttCtrl.Object, sender.Object, shellHost.Object, config, CreatePersistenceMock(), service, null, _factory);

        // Press hotkey for Beta (index 1), which is not active
        Assert.Equal("agent:a1:main", AgentRegistry.ActiveSessionKey);
        svc.HandleHotkeyPressed(1);

        // Should have switched to Beta
        Assert.Equal("agent:a2:main", AgentRegistry.ActiveSessionKey);
        pttCtrl.Verify(p => p.StartRecording(), Times.Never);
    }

    [Fact]
    public void HotkeyPressed_InactiveThenActive_FirstSwitchThenRecord()
    {
        var agents = new List<AgentInfo>
        {
            MakeAgent("a1", "Alpha", isDefault: true),
            MakeAgent("a2", "Beta"),
        };
        AgentRegistry.SetAgents(agents);

        var pttCtrl = new Mock<IPttController>();
        var mockClient = new Mock<IGatewayClient>();
        var config = new AppConfig();
        var service = new TestableGatewayService(config, mockClient.Object);
        using var svc = new AgentHotkeyService(pttCtrl.Object, Mock.Of<ITextMessageSender>(), Mock.Of<IStreamShellHost>(), config, CreatePersistenceMock(), service, null, _factory);

        // First press: switch to Beta
        svc.HandleHotkeyPressed(1);
        Assert.Equal("agent:a2:main", AgentRegistry.ActiveSessionKey);

        // Second press: Beta is now active → start recording
        svc.HandleHotkeyPressed(1);
        pttCtrl.Verify(p => p.StartRecording(), Times.Once);
    }

    [Fact]
    public void HotkeyReleased_HoldToTalkActiveAgent_StopsRecording()
    {
        var agents = new List<AgentInfo> { MakeAgent("a1", "Alpha", isDefault: true) };
        AgentRegistry.SetAgents(agents);

        var pttCtrl = new Mock<IPttController>();
        var cfg = new AppConfig { HoldToTalk = true };
        var mockClient = new Mock<IGatewayClient>();
        var service = new TestableGatewayService(cfg, mockClient.Object);
        using var svc = new AgentHotkeyService(pttCtrl.Object, Mock.Of<ITextMessageSender>(), Mock.Of<IStreamShellHost>(), cfg, CreatePersistenceMock(), service, null, _factory);

        svc.HandleHotkeyReleased(0);

        pttCtrl.Verify(p => p.StopRecording(), Times.Once);
    }

    [Fact]
    public void HotkeyReleased_NotHoldToTalk_DoesNotStop()
    {
        var agents = new List<AgentInfo> { MakeAgent("a1", "Alpha", isDefault: true) };
        AgentRegistry.SetAgents(agents);

        var pttCtrl = new Mock<IPttController>();
        var cfg = new AppConfig { HoldToTalk = false };
        var mockClient = new Mock<IGatewayClient>();
        var service = new TestableGatewayService(cfg, mockClient.Object);
        using var svc = new AgentHotkeyService(pttCtrl.Object, Mock.Of<ITextMessageSender>(), Mock.Of<IStreamShellHost>(), cfg, CreatePersistenceMock(), service, null, _factory);

        svc.HandleHotkeyReleased(0);

        pttCtrl.Verify(p => p.StopRecording(), Times.Never);
    }

    [Fact]
    public void HotkeyPressed_IndexOutOfRange_DoesNothing()
    {
        var agents = new List<AgentInfo> { MakeAgent("a1", "Alpha", isDefault: true) };
        AgentRegistry.SetAgents(agents);

        var pttCtrl = new Mock<IPttController>();
        var mockClient = new Mock<IGatewayClient>();
        var config = new AppConfig();
        var service = new TestableGatewayService(config, mockClient.Object);
        using var svc = new AgentHotkeyService(pttCtrl.Object, Mock.Of<ITextMessageSender>(), Mock.Of<IStreamShellHost>(), config, CreatePersistenceMock(), service, null, _factory);

        svc.HandleHotkeyPressed(5); // out of range
        pttCtrl.Verify(p => p.StartRecording(), Times.Never);
    }


}

/// <summary>
/// Disables parallelization for AgentHotkeyService tests because AgentRegistry
/// is a static singleton and cannot be safely shared across concurrent test threads.
/// </summary>
[CollectionDefinition("AgentHotkeyService", DisableParallelization = true)]
public class AgentHotkeyServiceCollection : ICollectionFixture<object>
{
}

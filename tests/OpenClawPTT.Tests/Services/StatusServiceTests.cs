using Xunit;
using Moq;
using OpenClawPTT.Services;
using OpenClawPTT;
using OpenClawPTT.Services.StatusParts;

namespace OpenClawPTT.Tests;

[Collection("ConversationNaming")]
public class StatusServiceTests
{
    static StatusServiceTests()
    {
        AgentSettingsPersistenceLegacy.Initialize(Mock.Of<IAgentSettingsPersistence>());
    }

    [Fact]
    public void SetGatewayStatus_ShowsGreenDot()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);

        service.SetGatewayStatus("Connected", StatusColor.Green);

        Assert.Contains("GW:", host.LastSeparatorRightText);
        Assert.Contains("[green]", host.LastSeparatorRightText);
        Assert.Contains("\u25CF", host.LastSeparatorRightText); // ●
    }

    [Fact]
    public void SetTtsStatus_ShowsRedDot()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);

        service.SetTtsStatus("Disconnected", StatusColor.Red);

        Assert.Contains("TTS:", host.LastSeparatorRightText);
        Assert.Contains("[red]", host.LastSeparatorRightText);
        Assert.Contains("\u25CF", host.LastSeparatorRightText); // ●
    }

    [Fact]
    public void MultipleUpdates_DotsShown()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);

        service.SetGatewayStatus("Connected", StatusColor.Green);
        service.SetTtsStatus("Starting", StatusColor.Yellow);

        // Should show labels + green dot + yellow animating dot
        Assert.Contains("GW:", host.LastSeparatorRightText);
        Assert.Contains("TTS:", host.LastSeparatorRightText);
        Assert.Contains("[green]", host.LastSeparatorRightText);
        Assert.Contains("[yellow]", host.LastSeparatorRightText);
        // Yellow dot animates — first frame is '·'
        Assert.Contains("\u00B7", host.LastSeparatorRightText); // ·
    }

    [Fact]
    public void SetDirectLlmStatus_UpdatesRenderedText()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);

        service.SetDirectLlmStatus("OK", StatusColor.Green);

        Assert.Contains("LLM:", host.LastSeparatorRightText);
        Assert.Contains("OK", host.LastSeparatorRightText);
        Assert.Contains("green", host.LastSeparatorRightText);
    }

    [Fact]
    public void SetDirectLlmLastCalled_ShowsElapsedTime()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);

        service.SetDirectLlmStatus("OK", StatusColor.Green);
        service.SetDirectLlmLastCalled(DateTime.Now);

        Assert.Contains("LLM:", host.LastSeparatorRightText);
        Assert.Contains("OK", host.LastSeparatorRightText);
        Assert.Contains("0s", host.LastSeparatorRightText);
    }

    [Fact]
    public void ThreadSafe_ConcurrentCalls_NoCrash()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);

        var t1 = Task.Run(() => {
            for (int i = 0; i < 100; i++)
                service.SetGatewayStatus("Status" + i, StatusColor.Green);
        });
        var t2 = Task.Run(() => {
            for (int i = 0; i < 100; i++)
                service.SetTtsStatus("Status" + i, StatusColor.Red);
        });

        var ex = Record.Exception(() => Task.WaitAll(t1, t2));
        Assert.Null(ex);
    }

    [Fact]
    public void DisposedHost_DoesNotCrash()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);
        host.Dispose();

        var ex = Record.Exception(() => service.SetGatewayStatus("Test", StatusColor.Green));
        Assert.Null(ex);
    }

    [Fact]
    public void Constructor_ThrowsOnNullHost()
    {
        Assert.Throws<ArgumentNullException>(() => new StatusService(null!));
    }

    // ── Agent status left-side tests ─────────────────────────────────────────

    [Fact]
    public void LeftText_Empty_WhenNoTrackerProvided()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);

        service.SetGatewayStatus("Connected", StatusColor.Green);

        Assert.Equal(string.Empty, host.LastSeparatorLeftText);
    }

    [Fact]
    public void LeftText_ShowsAgentInfo_WhenTrackerHasAgent()
    {
        var host = new FakeStreamShellHost();
        var tracker = new FakeAgentStatusTracker();
        using var service = new StatusService(host, tracker);

        // Register the agent in the global registry
        var agent = new AgentInfo
        {
            AgentId = "test-agent",
            Name = "Spelly",
            IsDefault = true,
            SessionKey = "agent:test-agent:main"
        };
        AgentRegistry.SetAgents(new[] { agent });

        // Seed the tracker with a snapshot
        var snapshot = new AgentStatusSnapshot
        {
            SessionKey = "agent:test-agent:main",
            DisplayName = "Spelly",
            Status = "running",
            Model = "deepseek/deepseek-v4-flash",
            ThinkingDefault = "high",
            ContextTokens = 200_000,
            TotalTokens = 12_000,
            InputTokens = 12_000
        };
        tracker.AddSnapshot(snapshot);

        // Manually trigger changed event
        tracker.FireChanged();

        Assert.NotNull(host.LastSeparatorLeftText);
        Assert.Contains("Spelly", host.LastSeparatorLeftText);
        Assert.Contains("🟢", host.LastSeparatorLeftText);
        Assert.Contains("deepseek-v4-flash", host.LastSeparatorLeftText);
        Assert.Contains("high", host.LastSeparatorLeftText);
        Assert.Contains("6.0%", host.LastSeparatorLeftText);
        Assert.Contains("12k", host.LastSeparatorLeftText);
        Assert.Contains("200k", host.LastSeparatorLeftText);
    }

    [Fact]
    public void LeftText_ShowsCorrectPercentage_ForVariousTokenSizes()
    {
        var host = new FakeStreamShellHost();
        var tracker = new FakeAgentStatusTracker();
        using var service = new StatusService(host, tracker);

        var agent = new AgentInfo
        {
            AgentId = "test-agent",
            Name = "K2.6",
            IsDefault = true,
            SessionKey = "agent:test-agent:main"
        };
        AgentRegistry.SetAgents(new[] { agent });

        var snapshot = new AgentStatusSnapshot
        {
            SessionKey = "agent:test-agent:main",
            DisplayName = "K2.6",
            Status = "done",
            Model = "kimi/kimi-k2.6",
            ContextTokens = 1_000_000,
            TotalTokens = 264_000,
        };
        tracker.AddSnapshot(snapshot);
        tracker.FireChanged();

        Assert.NotNull(host.LastSeparatorLeftText);
        Assert.Contains("K2.6", host.LastSeparatorLeftText);
        // 264k/1M = 26.4%, >= 10% so rounds to F0 → 26%
        Assert.Contains("26%", host.LastSeparatorLeftText);
        Assert.Contains("264k", host.LastSeparatorLeftText);
        Assert.Contains("1.0M", host.LastSeparatorLeftText);
    }

    [Fact]
    public void LeftText_Updates_WhenTrackerFiresChanged()
    {
        var host = new FakeStreamShellHost();
        var tracker = new FakeAgentStatusTracker();
        using var service = new StatusService(host, tracker);

        // First update — snapshot with Running
        var agent = new AgentInfo
        {
            AgentId = "test-agent",
            Name = "Spelly",
            IsDefault = true,
            SessionKey = "agent:test-agent:main"
        };
        AgentRegistry.SetAgents(new[] { agent });

        var runningSnapshot = new AgentStatusSnapshot
        {
            SessionKey = "agent:test-agent:main",
            DisplayName = "Spelly",
            Status = "running",
            Model = "deepseek/deepseek-v4-flash"
        };
        tracker.AddSnapshot(runningSnapshot);
        tracker.FireChanged();
        Assert.Contains("🟢", host.LastSeparatorLeftText);

        // Second update — tool-use state (stopReason == "toolUse" → 🔄)
        var toolSnapshot = new AgentStatusSnapshot
        {
            SessionKey = "agent:test-agent:main",
            DisplayName = "Spelly",
            Status = "running",
            StopReason = "toolUse",
            Model = "deepseek/deepseek-v4-flash"
        };
        tracker.AddSnapshot(toolSnapshot);
        tracker.FireChanged();
        Assert.Contains("🔄", host.LastSeparatorLeftText);
    }

    [Fact]
    public void LeftText_Empty_WhenTrackerHasNoAgent()
    {
        var host = new FakeStreamShellHost();
        var tracker = new FakeAgentStatusTracker();
        using var service = new StatusService(host, tracker);

        // Tracker has no agents
        tracker.FireChanged();

        Assert.Equal(string.Empty, host.LastSeparatorLeftText);
    }

    [Fact]
    public void LeftText_OmitsTokenInfo_WhenContextTokensIsNull()
    {
        var host = new FakeStreamShellHost();
        var tracker = new FakeAgentStatusTracker();
        using var service = new StatusService(host, tracker);

        var agent = new AgentInfo
        {
            AgentId = "test-agent",
            Name = "Spelly",
            IsDefault = true,
            SessionKey = "agent:test-agent:main"
        };
        AgentRegistry.SetAgents(new[] { agent });

        var snapshot = new AgentStatusSnapshot
        {
            SessionKey = "agent:test-agent:main",
            DisplayName = "Spelly",
            Status = "running",
            Model = "deepseek/deepseek-v4-flash",
            ContextTokens = null,
            TotalTokens = 12_000,
        };
        tracker.AddSnapshot(snapshot);
        tracker.FireChanged();

        Assert.NotNull(host.LastSeparatorLeftText);
        Assert.DoesNotContain("%", host.LastSeparatorLeftText);
        Assert.DoesNotContain("12k", host.LastSeparatorLeftText);
    }

    [Fact]
    public void LeftText_ReflectsActiveSessionChange()
    {
        // Reset static state before this test to avoid contamination from other tests
        AgentRegistry.SetAgents(Array.Empty<AgentInfo>());

        var host = new FakeStreamShellHost();
        var tracker = new FakeAgentStatusTracker();
        using var service = new StatusService(host, tracker);

        // Register two agents
        var agentA = new AgentInfo
        {
            AgentId = "agent-a",
            Name = "Alpha",
            IsDefault = true,
            SessionKey = "agent:agent-a:main"
        };
        var agentB = new AgentInfo
        {
            AgentId = "agent-b",
            Name = "Beta",
            IsDefault = false,
            SessionKey = "agent:agent-b:main"
        };
        AgentRegistry.SetAgents(new[] { agentA, agentB });

        // Seed snapshots for both
        var snapshotA = new AgentStatusSnapshot
        {
            SessionKey = "agent:agent-a:main",
            DisplayName = "Alpha",
            Status = "running",
            Model = "deepseek/deepseek-v4-flash"
        };
        var snapshotB = new AgentStatusSnapshot
        {
            SessionKey = "agent:agent-b:main",
            DisplayName = "Beta",
            Status = "running",
            Model = "kimi/kimi-k2.6"
        };
        tracker.AddSnapshot(snapshotA);
        tracker.AddSnapshot(snapshotB);
        tracker.FireChanged();

        // Default active is agent-a (IsDefault = true)
        Assert.Contains("Alpha", host.LastSeparatorLeftText);

        // Switch to agent-b
        AgentRegistry.SetActiveSession("agent:agent-b:main");
        Assert.Contains("Beta", host.LastSeparatorLeftText);

        // Switch back to agent-a
        AgentRegistry.SetActiveSession("agent:agent-a:main");
        Assert.Contains("Alpha", host.LastSeparatorLeftText);
    }

    [Fact]
    public void ConcurrentStatusUpdates_WithTracker_NoCrash()
    {
        var host = new FakeStreamShellHost();
        var tracker = new FakeAgentStatusTracker();
        using var service = new StatusService(host, tracker);

        var agent = new AgentInfo
        {
            AgentId = "test-agent",
            Name = "Spelly",
            IsDefault = true,
            SessionKey = "agent:test-agent:main"
        };
        AgentRegistry.SetAgents(new[] { agent });

        var snapshot = new AgentStatusSnapshot
        {
            SessionKey = "agent:test-agent:main",
            DisplayName = "Spelly",
            Status = "running",
            Model = "deepseek/deepseek-v4-flash"
        };
        tracker.AddSnapshot(snapshot);

        var t1 = Task.Run(() => {
            for (int i = 0; i < 50; i++)
                service.SetGatewayStatus("GW" + i, StatusColor.Green);
        });
        var t2 = Task.Run(() => {
            for (int i = 0; i < 50; i++)
                tracker.FireChanged();
        });

        var ex = Record.Exception(() => Task.WaitAll(t1, t2));
        Assert.Null(ex);
    }

    [Fact]
    public void ModelPart_ShortensProviderPrefix()
    {
        var part = new ModelPart();
        part.Update("deepseek/deepseek-v4-flash");
        Assert.Equal("deepseek-v4-flash", part.GetText());

        part.Update("kimi/kimi-k2.6");
        Assert.Equal("kimi-k2.6", part.GetText());

        part.Update("openai/gpt-4");
        Assert.Equal("openai/gpt-4", part.GetText());
    }

    [Fact]
    public void ModelPart_IsDirtyTracking_Works()
    {
        var part = new ModelPart();
        Assert.True(part.IsDirty); // starts dirty

        part.GetText();
        part.MarkClean();
        Assert.False(part.IsDirty);

        part.Update("different-model");
        Assert.True(part.IsDirty);
    }

    [Fact]
    public void ModelPart_Update_OnlyMarksDirtyOnChange()
    {
        var part = new ModelPart();
        part.Update("same-model");
        part.GetText();
        part.MarkClean();

        part.Update("same-model");
        Assert.False(part.IsDirty);
    }

    [Fact]
    public void ContextPart_FormatsTokenCount()
    {
        var part = new ContextPart();
        part.Update(200_000, 12_000);
        Assert.Contains("12k", part.GetText());
        Assert.Contains("200k", part.GetText());
        Assert.Contains("6.0%", part.GetText());

        part.Update(1_000_000, 264_000);
        Assert.Contains("264k", part.GetText());
        Assert.Contains("1.0M", part.GetText());
        Assert.Contains("26%", part.GetText());

        part.Update(1000, 250);
        Assert.Contains("250", part.GetText());
        Assert.DoesNotContain("M", part.GetText());
    }

    [Fact]
    public void ContextPart_Empty_WhenTokensNull()
    {
        var part = new ContextPart();
        part.Update(null, null);
        Assert.Equal(string.Empty, part.GetText());

        part.Update(100, null);
        Assert.Equal(string.Empty, part.GetText());
    }

    [Fact]
    public void ContextPart_IsDirtyTracking_Works()
    {
        var part = new ContextPart();
        part.Update(200_000, 12_000);
        Assert.True(part.IsDirty);

        part.GetText();
        part.MarkClean();
        Assert.False(part.IsDirty);

        part.Update(100_000, 5_000);
        Assert.True(part.IsDirty);
    }

    [Fact]
    public void ActiveAgentPart_IsDirtyTracking_Works()
    {
        var snapshot = new AgentStatusSnapshot
        {
            SessionKey = "agent:test:main",
            DisplayName = "TestAgent",
            Status = "running",
        };

        var part = new ActiveAgentPart();
        Assert.True(part.IsDirty); // starts dirty

        part.Update(snapshot);
        part.GetText();
        part.MarkClean();
        Assert.False(part.IsDirty);

        part.Update(snapshot);
        Assert.True(part.IsDirty);
    }

    [Fact]
    public void ThinkingLevelPart_IsDirtyTracking_Works()
    {
        var part = new ThinkingLevelPart();
        part.Update("high");
        Assert.True(part.IsDirty);

        part.GetText();
        part.MarkClean();
        Assert.False(part.IsDirty);

        part.Update("low");
        Assert.True(part.IsDirty);
    }

    [Fact]
    public void ConversationNamePart_IsDirtyTracking_Works()
    {
        var part = new ConversationNamePart();
        part.Update("TestConv");
        part.GetText();
        part.MarkClean();
        Assert.False(part.IsDirty);

        part.Update("NewConv");
        Assert.True(part.IsDirty);

        part.MarkClean();
        Assert.False(part.IsDirty);

        part.Update("NewConv");
        Assert.False(part.IsDirty);
    }

    [Fact]
    public void ServiceStatusPart_ShowsLabelPrefixWithColoredDot()
    {
        var part = new ServiceStatusPart("GW:");
        part.SetStatus(StatusColor.Green);

        string text = part.GetText();
        Assert.Contains("GW:", text);
        Assert.Contains("[green]", text);
        Assert.Contains("\u25CF", text); // ●

        part.SetStatus(StatusColor.Red);
        text = part.GetText();
        Assert.Contains("GW:", text);
        Assert.Contains("[red]", text);
        Assert.Contains("\u25CF", text); // ●
    }

    [Fact]
    public void ServiceStatusPart_IsDirtyTracking_Works()
    {
        var part = new ServiceStatusPart("TEST:");
        part.SetStatus(StatusColor.Green);
        Assert.True(part.IsDirty); // starts dirty

        part.GetText();
        part.MarkClean();
        Assert.False(part.IsDirty);

        part.SetStatus(StatusColor.Green);
        Assert.False(part.IsDirty); // same color, stays clean

        part.SetStatus(StatusColor.Red);
        Assert.True(part.IsDirty); // color changed
    }

    [Fact]
    public void SetSttStatus_ShowsDotOnRightText()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);

        service.SetSttStatus("Connected", StatusColor.Green);

        Assert.Contains("STT:", host.LastSeparatorRightText);
        Assert.Contains("[green]", host.LastSeparatorRightText);
        Assert.Contains("\u25CF", host.LastSeparatorRightText); // ●
    }

    [Fact]
    public void ServiceStatusPart_YellowDot_AnimatesThroughFrames()
    {
        var part = new ServiceStatusPart("STT:");
        part.SetStatus(StatusColor.Yellow);

        // Yellow state: animation advances via AdvanceFrame() then GetText()
        // Frame 0: '·'
        string text1 = part.GetText();
        Assert.Contains("STT:", text1);
        Assert.Contains("\u00B7", text1); // ·
        Assert.Contains("[yellow]", text1);

        // Advance + get: frame 1 → '•'
        part.AdvanceFrame();
        string text2 = part.GetText();
        Assert.Contains("\u2022", text2); // •

        // Advance + get: frame 2 → '●'
        part.AdvanceFrame();
        string text3 = part.GetText();
        Assert.Contains("\u25CF", text3); // ●

        // Advance + get: frame 3 → '•'
        part.AdvanceFrame();
        string text4 = part.GetText();
        Assert.Contains("\u2022", text4); // •

        // Advance + get: wraps around to frame 0 → '·'
        part.AdvanceFrame();
        string text5 = part.GetText();
        Assert.Contains("\u00B7", text5); // ·

        // Yellow always stays dirty (for continued rendering)
        Assert.True(part.IsDirty);

        // Switch to green — stops animation
        part.SetStatus(StatusColor.Green);
        string text6 = part.GetText();
        Assert.Contains("STT:", text6);
        Assert.Contains("\u25CF", text6); // ● (solid, no animation)
    }

    [Fact]
    public void DirectLlmStatusPart_BuildsCorrectText()
    {
        var part = new DirectLlmStatusPart();
        part.SetStatus("OK", StatusColor.Green);

        string text = part.GetText();
        Assert.Contains("LLM:", text);
        Assert.Contains("OK", text);
        Assert.Contains("green", text);
    }

    [Fact]
    public void DirectLlmStatusPart_IsDirtyTracking_Works()
    {
        var part = new DirectLlmStatusPart();
        part.SetStatus("OK", StatusColor.Green);
        Assert.True(part.IsDirty); // starts dirty

        part.GetText();
        part.MarkClean();
        Assert.False(part.IsDirty);

        part.SetStatus("OK", StatusColor.Green);
        Assert.False(part.IsDirty); // no change, stays clean

        part.SetStatus("Failed", StatusColor.Red);
        Assert.True(part.IsDirty); // value changed
    }

    [Fact]
    public void DirectLlmStatusPart_LastCalled_AlwaysMarksDirty()
    {
        var part = new DirectLlmStatusPart();
        part.SetStatus("OK", StatusColor.Green);
        part.GetText();
        part.MarkClean();
        Assert.False(part.IsDirty);

        part.SetLastCalled(DateTime.Now);
        Assert.True(part.IsDirty);

        part.GetText();
        part.MarkClean();

        // Setting timestamp again (time changed) marks dirty
        part.SetLastCalled(DateTime.Now);
        Assert.True(part.IsDirty);
    }

    [Fact]
    public void StatusPart_Position_CanBeChanged()
    {
        var part = new ModelPart();
        Assert.Equal(DisplayPosition.TopSeparatorLeft, part.Position);

        part.Position = DisplayPosition.TopSeparatorRight;
        Assert.Equal(DisplayPosition.TopSeparatorRight, part.Position);
    }

    [Fact]
    public void StatusPart_Position_Change_AlsoMarksDirty()
    {
        var part = new ModelPart();
        part.Update("test-model");
        part.GetText();
        part.MarkClean();
        Assert.False(part.IsDirty);

        part.Position = DisplayPosition.TopSeparatorRight;
        Assert.True(part.IsDirty);
    }

    [Fact]
    public void ApplyConfigPositions_SetsAllPartPositions()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);

        var cfg = new AppConfig
        {
            ActiveAgentPosition = DisplayPosition.TopSeparatorRight,
            ModelPosition = DisplayPosition.None,
            ThinkingLevelPosition = DisplayPosition.TopSeparatorLeft,
            ContextPosition = DisplayPosition.None,
            ConversationNamePosition = DisplayPosition.None,
            ConnectionStatusPosition = DisplayPosition.TopSeparatorLeft,
        };

        service.ApplyConfigPositions(cfg);

        var tracker = new FakeAgentStatusTracker();
        service.SetAgentStatusTracker(tracker);

        var agent = new AgentInfo
        {
            AgentId = "test",
            Name = "TestAgent",
            IsDefault = true,
            SessionKey = "agent:test:main"
        };
        AgentRegistry.SetAgents(new[] { agent });
        tracker.AddSnapshot(new AgentStatusSnapshot
        {
            SessionKey = "agent:test:main",
            DisplayName = "TestAgent",
            Status = "running",
            Model = "test-model",
            ThinkingDefault = "high",
        });
        tracker.FireChanged();

        service.SetGatewayStatus("Connected", StatusColor.Green);
        service.SetTtsStatus("Active", StatusColor.Green);

        Assert.NotNull(host.LastSeparatorLeftText);
        Assert.Contains("high", host.LastSeparatorLeftText);
        Assert.Contains("[green]", host.LastSeparatorLeftText);
        Assert.Contains("\u25CF", host.LastSeparatorLeftText); // ●
    }

    [Fact]
    public void StatusPart_Caching_ReturnsCachedStringWhenNotDirty()
    {
        var part = new ModelPart();
        part.Update("test-model");

        string first = part.GetText();
        part.MarkClean();
        string second = part.GetText();

        Assert.Equal(first, second);
        Assert.Same(first, second);
    }

    [Fact]
    public void StatusPart_Caching_ReturnsFreshOnDirty()
    {
        var part = new ModelPart();
        part.Update("first-model");
        string first = part.GetText();
        part.MarkClean();

        part.Update("second-model");
        string second = part.GetText();

        Assert.NotEqual(first, second);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void Dispose_UnsubscribesFromTrackerEvents()
    {
        var host = new FakeStreamShellHost();
        var tracker = new FakeAgentStatusTracker();
        var service = new StatusService(host, tracker);

        var agent = new AgentInfo
        {
            AgentId = "test-agent",
            Name = "Spelly",
            IsDefault = true,
            SessionKey = "agent:test-agent:main"
        };
        AgentRegistry.SetAgents(new[] { agent });
        var snapshot = new AgentStatusSnapshot
        {
            SessionKey = "agent:test-agent:main",
            DisplayName = "Spelly",
            Status = "running",
        };
        tracker.AddSnapshot(snapshot);

        service.Dispose();

        // After dispose, tracker events should not cause crashes
        var ex = Record.Exception(() => tracker.FireChanged());
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_UnsubscribesFromActiveSessionChanged()
    {
        var host = new FakeStreamShellHost();
        var tracker = new FakeAgentStatusTracker();
        var service = new StatusService(host, tracker);

        // Set up agents so SetActiveSession can actually fire the event
        var agent = new AgentInfo
        {
            AgentId = "agent-a",
            Name = "Alpha",
            IsDefault = true,
            SessionKey = "agent:agent-a:main"
        };
        AgentRegistry.SetAgents(new[] { agent });
        var snapshot = new AgentStatusSnapshot
        {
            SessionKey = "agent:agent-a:main",
            DisplayName = "Alpha",
            Status = "running",
        };
        tracker.AddSnapshot(snapshot);
        tracker.FireChanged();

        service.Dispose();

        // Firing ActiveSessionChanged on a disposed service should not crash.
        // Setting same session again won't fire (no change), so register a second
        // agent and switch to it after dispose.
        var agentB = new AgentInfo
        {
            AgentId = "agent-b",
            Name = "Beta",
            IsDefault = false,
            SessionKey = "agent:agent-b:main"
        };
        AgentRegistry.SetAgents(new[] { agent, agentB });

        var ex = Record.Exception(() => AgentRegistry.SetActiveSession("agent:agent-b:main"));
        Assert.Null(ex);
    }
}

// ── Test helpers ─────────────────────────────────────────────────────────────

/// <summary>
/// Fake implementation of IAgentStatusTracker for testing.
/// Maintains a simple in-memory store of snapshots and fires Changed on demand.
/// </summary>
public sealed class FakeAgentStatusTracker : IAgentStatusTracker
{
    private readonly Dictionary<string, AgentStatusSnapshot> _snapshots = new();

    public event Action? Changed;

    public IReadOnlyList<AgentStatusSnapshot> All => _snapshots.Values.ToList().AsReadOnly();

    public void Update(AgentStatusSnapshot snapshot)
    {
        _snapshots[snapshot.SessionKey] = snapshot;
    }

    public void Remove(string sessionKey)
    {
        _snapshots.Remove(sessionKey);
    }

    public void Reset(string sessionKey)
    {
        if (!_snapshots.TryGetValue(sessionKey, out var existing))
            return;

        var reset = existing with
        {
            RunId = null,
            Phase = null,
            Stream = null,
            EventReason = null,
            Seq = null,
            Status = null,
            StopReason = null,
            AbortedLastRun = null,
            SubagentRunState = null,
            HasActiveSubagentRun = null,
            InputTokens = null,
            OutputTokens = null,
            TotalTokens = null,
            TotalTokensFresh = null,
            ContextTokens = null,
            EstimatedCostUsd = null,
            StartedAt = null,
            EndedAt = null,
            RuntimeMs = null,
            UpdatedAt = null,
            SubagentRole = null,
            SpawnDepth = null,
            SubagentControlScope = null,
            SpawnedWorkspaceDir = null,
            ChildSessions = Array.Empty<string>(),
            CompactionCheckpointCount = null,
            LatestCompactionCheckpointId = null,
            LatestCompactionCheckpointCreatedAt = null,
            SystemSent = null,
            ThinkingDefault = null,
        };

        _snapshots[sessionKey] = reset;
    }

    public AgentStatusSnapshot? Get(string sessionKey)
    {
        return _snapshots.TryGetValue(sessionKey, out var s) ? s : null;
    }

    public AgentStatusSnapshot? GetMainAgent()
    {
        return _snapshots.Values.FirstOrDefault(s => !s.IsSubagent);
    }

    public IReadOnlyList<AgentStatusSnapshot> GetSubagents(string parentSessionKey)
    {
        return _snapshots.Values
            .Where(s => s.ParentSessionKey == parentSessionKey)
            .ToList().AsReadOnly();
    }

    /// <summary>Add or update a snapshot without firing Changed.</summary>
    public void AddSnapshot(AgentStatusSnapshot snapshot)
    {
        _snapshots[snapshot.SessionKey] = snapshot;
    }

    /// <summary>Manually fire the Changed event (simulating a tracker notification).</summary>
    public void FireChanged()
    {
        try
        {
            Changed?.Invoke();
        }
        catch (ObjectDisposedException)
        {
            // Ignore disposed subscriptions
        }
    }
}

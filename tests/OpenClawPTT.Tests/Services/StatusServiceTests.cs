using Xunit;
using Moq;
using OpenClawPTT.Services;
using OpenClawPTT;

namespace OpenClawPTT.Tests;

[Collection("ConversationNaming")]
public class StatusServiceTests
{
    static StatusServiceTests()
    {
        AgentSettingsPersistenceLegacy.Initialize(Mock.Of<IAgentSettingsPersistence>());
    }

    [Fact]
    public void SetGatewayStatus_UpdatesRenderedText()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);

        service.SetGatewayStatus("Connected", StatusColor.Green);

        Assert.Contains("GW:", host.LastSeparatorRightText);
        Assert.Contains("Connected", host.LastSeparatorRightText);
        Assert.Contains("green", host.LastSeparatorRightText);
    }

    [Fact]
    public void SetTtsStatus_UpdatesRenderedText()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);

        service.SetTtsStatus("Disconnected", StatusColor.Red);

        Assert.Contains("TTS:", host.LastSeparatorRightText);
        Assert.Contains("Disconnected", host.LastSeparatorRightText);
        Assert.Contains("red", host.LastSeparatorRightText);
    }

    [Fact]
    public void MultipleUpdates_BothShown()
    {
        var host = new FakeStreamShellHost();
        var service = new StatusService(host);

        service.SetGatewayStatus("Connected", StatusColor.Green);
        service.SetTtsStatus("Starting", StatusColor.Yellow);

        Assert.Contains("Connected", host.LastSeparatorRightText);
        Assert.Contains("Starting", host.LastSeparatorRightText);
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
    public void ShortenModelName_StripsDuplicateProviderPrefix()
    {
        // Access via reflection since these are private static methods
        var type = typeof(StatusService);
        var method = type.GetMethod("ShortenModelName",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        Assert.Equal("deepseek-v4-flash",
            method.Invoke(null, new object[] { "deepseek/deepseek-v4-flash" }));
        Assert.Equal("kimi-k2.6",
            method.Invoke(null, new object[] { "kimi/kimi-k2.6" }));
        // gpt-4 doesn't start with "openai", and model is <= 30 chars, so kept as-is
        Assert.Equal("openai/gpt-4",
            method.Invoke(null, new object[] { "openai/gpt-4" }));
    }

    [Fact]
    public void FormatTokenCount_FormatsCorrectly()
    {
        Assert.Equal("12k", StatusService.FormatTokenCount(12_000L));
        Assert.Equal("264k", StatusService.FormatTokenCount(264_000L));
        Assert.Equal("1k", StatusService.FormatTokenCount(1_000L));
        Assert.Equal("1.0M", StatusService.FormatTokenCount(1_000_000L));
        Assert.Equal("500", StatusService.FormatTokenCount(500L));
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

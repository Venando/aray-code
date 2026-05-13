using System.Text.Json;
using Moq;
using OpenClawPTT;
using Xunit;

namespace OpenClawPTT.Tests.Gateway;

[Collection("AgentRegistryCollection")]
public class SnapshotProcessorTests
{
    private readonly Mock<ILogger> _mockLogger;

    public SnapshotProcessorTests()
    {
        _mockLogger = new Mock<ILogger>();
    }

    private SnapshotProcessor CreateProcessor()
    {
        return new SnapshotProcessor(_mockLogger.Object);
    }

    // ─── ProcessSnapshot ─────────────────────────────────────────────

    [Fact]
    public void ProcessSnapshot_WithHealthAgents_SetsAgentRegistry()
    {
        var processor = CreateProcessor();
        var json = JsonDocument.Parse(/* lang=json */ """
            {"snapshot":{"health":{"agents":[{"agentId":"default","name":"Default Agent","isDefault":true}]}}}
            """).RootElement;

        processor.ProcessSnapshot(json);

        Assert.Equal("agent:default:main", AgentRegistry.ActiveSessionKey);
    }

    [Fact]
    public void ProcessSnapshot_NoSnapshot_DoesNotThrow()
    {
        var processor = CreateProcessor();
        var json = JsonDocument.Parse(/* lang=json */ """
            {}
            """).RootElement;

        var exception = Record.Exception(() => processor.ProcessSnapshot(json));
        Assert.Null(exception);
    }

    [Fact]
    public void ProcessSnapshot_NoHealthAgents_DoesNotThrow()
    {
        var processor = CreateProcessor();
        var json = JsonDocument.Parse(/* lang=json */ """
            {"snapshot":{"other":"data"}}
            """).RootElement;

        var exception = Record.Exception(() => processor.ProcessSnapshot(json));
        Assert.Null(exception);
    }

    [Fact]
    public void ProcessSnapshot_WithMultipleAgents_SetsAllAgents()
    {
        var processor = CreateProcessor();
        var json = JsonDocument.Parse(/* lang=json */ """
            {
                "snapshot": {
                    "health": {
                        "agents": [
                            {"agentId":"agent1","name":"Agent One","isDefault":false},
                            {"agentId":"agent2","name":"Agent Two","isDefault":true},
                            {"agentId":"agent3","name":"Agent Three","isDefault":false}
                        ]
                    }
                }
            }
            """).RootElement;

        processor.ProcessSnapshot(json);

        Assert.Equal(3, AgentRegistry.Agents.Count);
        Assert.Equal("agent:agent2:main", AgentRegistry.ActiveSessionKey); // Default agent becomes active
    }

    [Fact]
    public void ProcessSnapshot_LogsAgentsLoaded()
    {
        var processor = CreateProcessor();
        var json = JsonDocument.Parse(/* lang=json */ """
            {"snapshot":{"health":{"agents":[{"agentId":"test","name":"Test Agent","isDefault":true}]}}}
            """).RootElement;

        processor.ProcessSnapshot(json);

        _mockLogger.Verify(x => x.Log("gateway", It.Is<string>(s => s.Contains("Loaded") && s.Contains("agent(s)") && s.Contains("Active session:")), LogLevel.Info), Times.Once);
    }

    [Fact]
    public void ProcessSnapshot_LogsSnapshotPayloadAtVerboseLevel()
    {
        var processor = CreateProcessor();
        var json = JsonDocument.Parse(/* lang=json */ """
            {"snapshot":{"health":{"agents":[{"agentId":"test","name":"Test Agent","isDefault":true}]}}}
            """).RootElement;

        processor.ProcessSnapshot(json);

        _mockLogger.Verify(x => x.Log("ws", It.Is<string>(s => s.Contains("SERVER SNAPSHOT PAYLOAD")), LogLevel.Verbose), Times.AtLeastOnce);
    }

    [Fact]
    public void ProcessSnapshot_AgentInfo_HasCorrectSessionKey()
    {
        var processor = CreateProcessor();
        var json = JsonDocument.Parse(/* lang=json */ """
            {"snapshot":{"health":{"agents":[{"agentId":"myagent","name":"My Agent","isDefault":true}]}}}
            """).RootElement;

        processor.ProcessSnapshot(json);

        var agent = AgentRegistry.Agents[0];
        Assert.Equal("myagent", agent.AgentId);
        Assert.Equal("My Agent", agent.Name);
        Assert.Equal("agent:myagent:main", agent.SessionKey);
        Assert.True(agent.IsDefault);
    }
}

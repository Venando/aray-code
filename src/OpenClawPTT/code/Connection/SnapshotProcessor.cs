using System.Text.Json;
using System.Text.RegularExpressions;
using OpenClawPTT.Services;

namespace OpenClawPTT;

/// <summary>
/// Processes gateway snapshot data to update agent registry and status tracker.
/// </summary>
public sealed class SnapshotProcessor : ISnapshotProcessor
{
    private readonly ILogger _logger;
    private readonly IAgentStatusTracker? _agentStatusTracker;

    /// <summary>
    /// Creates a new SnapshotProcessor.
    /// </summary>
    /// <param name="logger">The logger for diagnostic output.</param>
    /// <param name="agentStatusTracker">Optional tracker to seed with snapshot agents.</param>
    public SnapshotProcessor(ILogger logger, IAgentStatusTracker? agentStatusTracker = null)
    {
        _logger = logger;
        _agentStatusTracker = agentStatusTracker;
    }

    /// <inheritdoc />
    public void ProcessSnapshot(JsonElement hello)
    {
        if (!hello.TryGetProperty("snapshot", out var snapshot))
            return;

        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string prettySnapshot = JsonSerializer.Serialize(snapshot, options);
            var lines = $"--- SERVER SNAPSHOT PAYLOAD ---\n{prettySnapshot}\n----------------------------".Split('\n');
            foreach (var line in lines) _logger.Log("ws", line, LogLevel.Verbose);
        }

        if (snapshot.TryGetProperty("health", out var health)
            && health.TryGetProperty("agents", out var agents))
        {
            var agentList = new List<AgentInfo>();
            foreach (JsonElement agent in agents.EnumerateArray())
            {
                string agentId = agent.GetProperty("agentId").GetString() ?? "";
                string name = agent.GetProperty("name").GetString() ?? "";
                bool isDefault = agent.GetProperty("isDefault").GetBoolean();
                string sessionKey = $"agent:{agentId}:main";

                agentList.Add(new AgentInfo
                {
                    AgentId = agentId,
                    Name = name,
                    IsDefault = isDefault,
                    SessionKey = sessionKey
                });

                // Seed the status tracker so the bottom panel shows agents immediately
                _agentStatusTracker?.Update(new AgentStatusSnapshot
                {
                    SessionKey = sessionKey,
                    DisplayName = name,
                    Status = "idle"
                });
            }

            AgentRegistry.SetAgents(agentList);
            _logger.Log("gateway", $"Loaded {agentList.Count} agent(s). Active session: {AgentRegistry.ActiveSessionKey}", LogLevel.Info);
        }
    }
}

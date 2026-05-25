using System.Text.Json;
using System.Text.RegularExpressions;
using ArayCode.Services;

namespace ArayCode;

/// <summary>
/// Processes gateway snapshot data to update agent registry and status tracker.
/// </summary>
public sealed class SnapshotProcessor : ISnapshotProcessor
{
    private readonly ILogger _logger;
    private readonly IAgentActivityStore? _activityStore;

    /// <summary>
    /// Creates a new SnapshotProcessor.
    /// </summary>
    /// <param name="logger">The logger for diagnostic output.</param>
    /// <param name="activityStore">Optional tracker to seed with snapshot agents.</param>
    public SnapshotProcessor(ILogger logger, IAgentActivityStore? activityStore = null)
    {
        _logger = logger;
        _activityStore = activityStore;
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
                if (!agent.TryGetProperty("agentId", out var agentIdEl))
                {
                    _logger.Log("gateway", "Snapshot agent missing 'agentId' — skipping malformed entry.", LogLevel.Info);
                    continue;
                }

                string name;

                if (!agent.TryGetProperty("name", out var nameEl))
                {
                    name = "Unnamed";
                }
                else
                {
                    name = nameEl.GetString() ?? "";
                }

                bool isDefault = false;
                
                if (!agent.TryGetProperty("isDefault", out var isDefaultEl))
                {
                    _logger.Log("gateway", $"Snapshot agent '{agentIdEl.GetString()}' missing 'isDefault'", LogLevel.Info);
                }
                else
                {
                    isDefault = isDefaultEl.GetBoolean();
                }

                string agentId = agentIdEl.GetString() ?? "";
                string sessionKey = $"agent:{agentId}:main";

                agentList.Add(new AgentInfo
                {
                    AgentId = agentId,
                    Name = name,
                    IsDefault = isDefault,
                    SessionKey = sessionKey
                });

                // Seed the activity store so the bottom panel shows agents immediately
                _activityStore?.Store(new SessionStateEvent
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

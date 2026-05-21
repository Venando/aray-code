namespace OpenClawPTT.Services;

/// <summary>
/// Builds the list of visible agents for the bottom panel.
/// Filters out cron sessions, non-main sessions, subagents, and agents
/// hidden via <c>ShowInStatusPanel</c>.
/// </summary>
public sealed class VisibleAgentListBuilder(IAgentActivityStore store)
{
    private readonly IAgentActivityStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public List<(string SessionKey, string? AgentId)> BuildVisibleAgents()
    {
        var visibleAgents = new List<(string SessionKey, string? AgentId)>();
        var trackedSessions = _store.GetTrackedSessions();

        foreach (var sk in trackedSessions)
        {
            if (sk.Contains("cron") || !sk.Contains("main"))
                continue;

            var state = _store.GetSessionState(sk);
            if (state is null) continue;

            // Skip subagents
            if (state.ParentSessionKey is not null || state.SpawnedBy is not null)
                continue;

            var agent = AgentRegistry.Agents.FirstOrDefault(
                a => a.SessionKey == sk);

            // Respect ShowInStatusPanel setting
            var show = agent is not null
                && AgentSettingsPersistenceLegacy.GetPersistedShowInStatusPanel(agent.AgentId);
            if (!show && agent is not null) continue;

            visibleAgents.Add((sk, agent?.AgentId));
        }

        return visibleAgents;
    }
}

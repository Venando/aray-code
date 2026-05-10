namespace OpenClawPTT.Services;

/// <summary>
/// Thread-safe in-memory store for agent and subagent status snapshots.
/// </summary>
public sealed class AgentStatusTracker : IAgentStatusTracker
{
    private readonly Dictionary<string, AgentStatusSnapshot> _snapshots = new();
    private readonly object _lock = new();

    public event Action? Changed;

    public void Update(AgentStatusSnapshot snapshot)
    {
        lock (_lock)
        {
            _snapshots[snapshot.SessionKey] = snapshot;
        }
        Changed?.Invoke();
    }

    public void Remove(string sessionKey)
    {
        bool removed;
        lock (_lock)
        {
            removed = _snapshots.Remove(sessionKey);
        }
        if (removed)
            Changed?.Invoke();
    }

    public AgentStatusSnapshot? Get(string sessionKey)
    {
        lock (_lock)
        {
            return _snapshots.TryGetValue(sessionKey, out var s) ? s : null;
        }
    }

    public IReadOnlyList<AgentStatusSnapshot> All
    {
        get
        {
            lock (_lock)
            {
                return _snapshots.Values.ToList().AsReadOnly();
            }
        }
    }

    public AgentStatusSnapshot? GetMainAgent()
    {
        lock (_lock)
        {
            return _snapshots.Values.FirstOrDefault(s => !s.IsSubagent);
        }
    }

    public IReadOnlyList<AgentStatusSnapshot> GetSubagents(string parentSessionKey)
    {
        lock (_lock)
        {
            return _snapshots.Values
                .Where(s => s.ParentSessionKey == parentSessionKey)
                .ToList().AsReadOnly();
        }
    }
}

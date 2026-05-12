namespace OpenClawPTT.Services;

/// <summary>
/// Thread-safe in-memory store for agent and subagent status snapshots.
/// <para>
/// <b>Non-erasure guarantee:</b> <see cref="Update"/> always passes the
/// previously stored snapshot to <see cref="AgentStatusExtractor.Extract"/>
/// so that information present in an earlier, richer payload is never
/// overwritten by a later partial payload.  The merge logic lives in the
/// extractor; the tracker is purely a thread-safe key-value store that
/// fires <see cref="Changed"/> on every write.
/// </para>
/// </summary>
public sealed class AgentStatusTracker : IAgentStatusTracker
{
    private readonly Dictionary<string, AgentStatusSnapshot> _snapshots = new();
    private readonly object _lock = new();
    private readonly List<AgentStatusSnapshot> _allCacheList = new();
    private System.Collections.ObjectModel.ReadOnlyCollection<AgentStatusSnapshot>? _allCacheReadOnly;
    private bool _allCacheDirty = true;

    public event Action? Changed;

    /// <summary>
    /// Stores or merges <paramref name="snapshot"/> into the tracker.
    /// The incoming snapshot is merged with the existing one (if any) so
    /// that no previously captured field is erased by a later partial event.
    /// </summary>
    public void Update(AgentStatusSnapshot snapshot)
    {
        bool changed;
        lock (_lock)
        {
            _snapshots.TryGetValue(snapshot.SessionKey, out var existing);

            // Merge with the existing snapshot so that fields absent in the
            // new payload are preserved from the old one.
            var merged = existing is null
                ? snapshot
                : AgentStatusExtractor.MergeSnapshots(existing, snapshot);

            changed = !ReferenceEquals(existing, merged)
                      && (existing is null || !existing.Equals(merged));

            _snapshots[snapshot.SessionKey] = merged;
            if (changed) _allCacheDirty = true;
        }

        if (changed)
            Changed?.Invoke();
    }

    public void Remove(string sessionKey)
    {
        bool removed;
        lock (_lock)
        {
            removed = _snapshots.Remove(sessionKey);
            if (removed) _allCacheDirty = true;
        }
        if (removed)
            Changed?.Invoke();
    }

    /// <summary>
    /// Resets the operational state of a tracked session while preserving
    /// identity-level fields (session key, model, display name, etc.).
    /// After reset the agent appears in a clean green-ready state.
    /// </summary>
    public void Reset(string sessionKey)
    {
        bool changed;
        lock (_lock)
        {
            if (!_snapshots.TryGetValue(sessionKey, out var existing))
                return;

            var reset = existing with
            {
                // ── Run / event envelope — clear ──
                RunId = null,
                Phase = null,
                Stream = null,
                EventReason = null,
                Seq = null,

                // ── Operational state — clear (agent returns to 🟢) ──
                Status = null,
                StopReason = null,
                AbortedLastRun = null,
                SubagentRunState = null,
                HasActiveSubagentRun = null,

                // ── Tokens & cost — clear ──
                InputTokens = null,
                OutputTokens = null,
                TotalTokens = null,
                TotalTokensFresh = null,
                ContextTokens = null,
                EstimatedCostUsd = null,

                // ── Timing — clear ──
                StartedAt = null,
                EndedAt = null,
                RuntimeMs = null,
                UpdatedAt = null,

                // ── Subagent metadata — clear ──
                SubagentRole = null,
                SpawnDepth = null,
                SubagentControlScope = null,
                SpawnedWorkspaceDir = null,
                ChildSessions = Array.Empty<string>(),

                // ── Compaction / context — clear ──
                CompactionCheckpointCount = null,
                LatestCompactionCheckpointId = null,
                LatestCompactionCheckpointCreatedAt = null,

                // ── Channel / delivery transient — clear ──
                SystemSent = null,
                ThinkingDefault = null,
            };

            _snapshots[sessionKey] = reset;
            _allCacheDirty = true;
            changed = true;
        }

        if (changed)
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
                if (_allCacheDirty)
                {
                    _allCacheList.Clear();
                    _allCacheList.AddRange(_snapshots.Values);
                    _allCacheReadOnly = _allCacheList.AsReadOnly();
                    _allCacheDirty = false;
                }
                return _allCacheReadOnly!;
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
using System.Linq;

namespace OpenClawPTT.Services;

/// <summary>
/// Merges two <see cref="AgentStatusSnapshot"/> values, ensuring that every
/// non-null field in the incoming snapshot wins, and every null field falls
/// back to the value in the existing snapshot (non-erasure guarantee).
/// </summary>
public static class AgentStatusMerger
{
    /// <summary>
    /// Merges <paramref name="incoming"/> into <paramref name="existing"/>,
    /// preserving previously captured fields that the incoming payload leaves null.
    /// </summary>
    public static AgentStatusSnapshot MergeSnapshots(
        AgentStatusSnapshot existing, AgentStatusSnapshot incoming)
        => Merge(existing, incoming);

    /// <summary>
    /// Returns a new snapshot where every non-null field in <paramref name="incoming"/>
    /// wins, and every null field in <paramref name="incoming"/> falls back to the
    /// value already stored in <paramref name="existing"/>.
    /// Special cases:
    /// <list type="bullet">
    ///   <item>
    ///     <c>ChildSessions</c> is merged as a union so that child session keys
    ///     accumulated across multiple events are never discarded.
    ///   </item>
    ///   <item>
    ///     <c>ParentSessionKey</c> is never overwritten with null/empty once set,
    ///     matching the original tracker guard.
    ///   </item>
    ///   <item>
    ///     Time-sensitive fields (<c>Status</c>, <c>StopReason</c>, <c>SubagentRunState</c>,
    ///     <c>HasActiveSubagentRun</c>, token counts, timing) always take the
    ///     incoming value when present — they represent the most recent state.
    ///   </item>
    /// </list>
    /// </summary>
    private static AgentStatusSnapshot Merge(AgentStatusSnapshot existing, AgentStatusSnapshot incoming)
    {
        // Merge child sessions as a union. Preserve the existing reference
        // when the union doesn't add anything new — this avoids spurious
        // reference changes that fool record equality in AgentStatusTracker.
        IReadOnlyList<string> mergedChildren;
        if (existing.ChildSessions.Count == 0)
        {
            mergedChildren = incoming.ChildSessions;
        }
        else if (incoming.ChildSessions.Count == 0)
        {
            mergedChildren = existing.ChildSessions;
        }
        else
        {
            var union = existing.ChildSessions
                .Union(incoming.ChildSessions, StringComparer.Ordinal)
                .ToList();
            mergedChildren = union.Count == existing.ChildSessions.Count
                && existing.ChildSessions.SequenceEqual(union, StringComparer.Ordinal)
                ? existing.ChildSessions
                : union.AsReadOnly();
        }

        return existing with
        {
            // Identity — keep existing if incoming is blank
            SessionId = incoming.SessionId ?? existing.SessionId,
            ParentSessionKey = (!string.IsNullOrEmpty(incoming.ParentSessionKey))
                                   ? incoming.ParentSessionKey
                                   : existing.ParentSessionKey,
            SpawnedBy = incoming.SpawnedBy ?? existing.SpawnedBy,
            DisplayName = incoming.DisplayName ?? existing.DisplayName,
            Kind = incoming.Kind ?? existing.Kind,

            // Run / event envelope — always reflect the latest event
            RunId = incoming.RunId ?? existing.RunId,
            Phase = incoming.Phase ?? existing.Phase,
            Stream = incoming.Stream ?? existing.Stream,
            EventReason = incoming.EventReason ?? existing.EventReason,
            Seq = incoming.Seq ?? existing.Seq,

            // Operational state — always take incoming when present (it's the latest truth)
            Status = incoming.Status ?? existing.Status,
            StopReason = incoming.StopReason ?? existing.StopReason,
            AbortedLastRun = incoming.AbortedLastRun ?? existing.AbortedLastRun,
            SubagentRunState = incoming.SubagentRunState ?? existing.SubagentRunState,
            HasActiveSubagentRun = incoming.HasActiveSubagentRun ?? existing.HasActiveSubagentRun,

            // Model — keep existing if incoming is absent
            Model = incoming.Model ?? existing.Model,
            ModelProvider = incoming.ModelProvider ?? existing.ModelProvider,
            AgentRuntimeId = incoming.AgentRuntimeId ?? existing.AgentRuntimeId,

            // Tokens — incoming wins when present
            InputTokens = incoming.InputTokens ?? existing.InputTokens,
            OutputTokens = incoming.OutputTokens ?? existing.OutputTokens,
            TotalTokens = incoming.TotalTokens ?? existing.TotalTokens,
            TotalTokensFresh = incoming.TotalTokensFresh ?? existing.TotalTokensFresh,
            ContextTokens = incoming.ContextTokens ?? existing.ContextTokens,
            EstimatedCostUsd = incoming.EstimatedCostUsd ?? existing.EstimatedCostUsd,

            // Timing — incoming wins when present
            StartedAt = incoming.StartedAt ?? existing.StartedAt,
            EndedAt = incoming.EndedAt ?? existing.EndedAt,
            RuntimeMs = incoming.RuntimeMs ?? existing.RuntimeMs,
            UpdatedAt = incoming.UpdatedAt ?? existing.UpdatedAt,

            // Subagent metadata
            SubagentRole = incoming.SubagentRole ?? existing.SubagentRole,
            SpawnDepth = incoming.SpawnDepth ?? existing.SpawnDepth,
            SubagentControlScope = incoming.SubagentControlScope ?? existing.SubagentControlScope,
            SpawnedWorkspaceDir = incoming.SpawnedWorkspaceDir ?? existing.SpawnedWorkspaceDir,
            ChildSessions = mergedChildren,

            // Channel / delivery
            Channel = incoming.Channel ?? existing.Channel,
            LastChannel = incoming.LastChannel ?? existing.LastChannel,
            ChatType = incoming.ChatType ?? existing.ChatType,
            OriginProvider = incoming.OriginProvider ?? existing.OriginProvider,
            SystemSent = incoming.SystemSent ?? existing.SystemSent,

            // Thinking / model options
            ThinkingDefault = incoming.ThinkingDefault ?? existing.ThinkingDefault,

            // Compaction
            CompactionCheckpointCount = incoming.CompactionCheckpointCount ?? existing.CompactionCheckpointCount,
            LatestCompactionCheckpointId = incoming.LatestCompactionCheckpointId ?? existing.LatestCompactionCheckpointId,
            LatestCompactionCheckpointCreatedAt = incoming.LatestCompactionCheckpointCreatedAt ?? existing.LatestCompactionCheckpointCreatedAt,
        };
    }
}

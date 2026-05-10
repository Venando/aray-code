using System.Text.Json;

namespace OpenClawPTT.Services;

/// <summary>
/// Extracts agent/subagent status snapshots from gateway event payloads.
/// <para>
/// Merge semantics: every field is updated only when the incoming payload
/// carries a non-null value, so a later partial payload never erases data
/// that was set by an earlier, richer one.  Call <see cref="Extract"/> with
/// the optional <paramref name="existing"/> snapshot to enable merging.
/// </para>
/// </summary>
public static class AgentStatusExtractor
{
    /// <summary>
    /// Attempts to build a snapshot from any gateway event payload.
    /// Returns null when no sessionKey is present.
    /// </summary>
    /// <param name="console">Colour console for optional debug output.</param>
    /// <param name="payload">Raw JSON element of the gateway event.</param>
    /// <param name="existing">
    /// Previous snapshot for this session, if any.
    /// When supplied, every field that the new payload leaves null is
    /// carried over from the existing snapshot, preventing information loss.
    /// </param>
    public static AgentStatusSnapshot? Extract(
        IColorConsole console,
        JsonElement payload,
        AgentStatusSnapshot? existing = null)
    {
        //console.Log("test", payload.ToString(), LogLevel.None);

        if (payload.ValueKind != JsonValueKind.Object)
            return null;

        // ── Top-level sessionKey is mandatory ─────────────────────────────────
        if (!payload.TryGetProperty("sessionKey", out var sessionKeyEl))
            return null;

        var sessionKey = sessionKeyEl.GetString();
        if (string.IsNullOrEmpty(sessionKey))
            return null;

        // ── Nested objects ────────────────────────────────────────────────────
        payload.TryGetProperty("session", out var session);   // may be default
        payload.TryGetProperty("message", out var message);   // may be default
        payload.TryGetProperty("data", out var data);         // lifecycle/tool/item stream wrapper

        // ── Field helpers ─────────────────────────────────────────────────────
        // Priority: top-level payload → nested session → nested data object.
        // The top-level copy is always the freshest (reflects state at emission
        // time), so it wins.  "data" is the stream-specific wrapper.
        string? Str(string k) => GetString(payload, k) ?? GetString(session, k) ?? GetString(data, k);
        long? Lng(string k) => GetLong(payload, k) ?? GetLong(session, k) ?? GetLong(data, k);
        bool? Bl(string k) => GetBool(payload, k) ?? GetBool(session, k) ?? GetBool(data, k);
        int? Int(string k) => GetInt(payload, k) ?? GetInt(session, k) ?? GetInt(data, k);

        // ── Parse agentRuntime → "{id}:{source}" ──────────────────────────────
        string? agentRuntimeId = null;
        if (TryGetObject(payload, "agentRuntime", out var ar) || TryGetObject(session, "agentRuntime", out ar))
        {
            var id = GetString(ar, "id");
            var src = GetString(ar, "source");
            if (id != null || src != null)
                agentRuntimeId = $"{id}:{src}";
        }

        // ── Parse origin → "{provider}:{surface}" ─────────────────────────────
        string? originProvider = null;
        if (TryGetObject(payload, "origin", out var origin) || TryGetObject(session, "origin", out origin))
        {
            var prov = GetString(origin, "provider");
            var surf = GetString(origin, "surface");
            if (prov != null || surf != null)
                originProvider = $"{prov}:{surf}";
        }

        // ── Parse latestCompactionCheckpoint ──────────────────────────────────
        string? latestCpId = null;
        long? latestCpCreatedAt = null;
        if (TryGetObject(payload, "latestCompactionCheckpoint", out var lcp)
            || TryGetObject(session, "latestCompactionCheckpoint", out lcp))
        {
            latestCpId = GetString(lcp, "checkpointId");
            latestCpCreatedAt = GetLong(lcp, "createdAt");
        }

        // ── Stop reason: prefer message.stopReason, then fall back ────────────
        var stopReason = GetString(message, "stopReason") ?? Str("stopReason");

        // ── Parent session key: prefer explicit field, fall back to spawnedBy ──
        var parentSessionKey = Str("parentSessionKey") ?? Str("spawnedBy");

        // ── estimatedCostUsd (stored as a JSON number) ────────────────────────
        decimal? estimatedCostUsd = GetDecimal(payload, "estimatedCostUsd")
                                 ?? GetDecimal(session, "estimatedCostUsd");

        // ── Child sessions ────────────────────────────────────────────────────
        var childSessions = GetStringArray(payload, "childSessions").Count > 0
            ? GetStringArray(payload, "childSessions")
            : GetStringArray(session, "childSessions");

        // ── Build the fresh snapshot ──────────────────────────────────────────
        var fresh = new AgentStatusSnapshot
        {
            SessionKey = sessionKey,
            SessionId = Str("sessionId"),
            ParentSessionKey = parentSessionKey,
            SpawnedBy = Str("spawnedBy"),
            DisplayName = Str("displayName"),
            Kind = Str("kind"),

            RunId = Str("runId"),
            Phase = Str("phase") ?? GetString(data, "phase"),
            Stream = Str("stream"),
            EventReason = Str("reason"),
            Seq = Int("seq"),

            Status = Str("status"),
            StopReason = stopReason,
            AbortedLastRun = Bl("abortedLastRun"),
            SubagentRunState = Str("subagentRunState"),
            HasActiveSubagentRun = Bl("hasActiveSubagentRun"),

            Model = Str("model"),
            ModelProvider = Str("modelProvider"),
            AgentRuntimeId = agentRuntimeId,

            InputTokens = Lng("inputTokens"),
            OutputTokens = Lng("outputTokens"),
            TotalTokens = Lng("totalTokens"),
            TotalTokensFresh = Bl("totalTokensFresh"),
            ContextTokens = Lng("contextTokens"),
            EstimatedCostUsd = estimatedCostUsd,

            StartedAt = Lng("startedAt") ?? GetLong(data, "startedAt") ?? GetLong(payload, "ts"),
            EndedAt = Lng("endedAt"),
            RuntimeMs = Lng("runtimeMs"),
            UpdatedAt = Lng("updatedAt") ?? GetLong(payload, "ts"),

            SubagentRole = Str("subagentRole"),
            SpawnDepth = Int("spawnDepth"),
            SubagentControlScope = Str("subagentControlScope"),
            SpawnedWorkspaceDir = Str("spawnedWorkspaceDir"),
            ChildSessions = childSessions,

            Channel = Str("channel"),
            LastChannel = Str("lastChannel"),
            ChatType = Str("chatType"),
            OriginProvider = originProvider,
            SystemSent = Bl("systemSent"),

            ThinkingDefault = Str("thinkingDefault"),

            CompactionCheckpointCount = Int("compactionCheckpointCount"),
            LatestCompactionCheckpointId = latestCpId,
            LatestCompactionCheckpointCreatedAt = latestCpCreatedAt,
        };

        // ── Merge with existing: never overwrite a known value with null ───────
        return existing is null ? fresh : Merge(existing, fresh);
    }

    // ── Merge ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Exposed as <c>internal</c> so <see cref="AgentStatusTracker"/> can
    /// call it when storing a pre-built snapshot.
    /// </summary>
    internal static AgentStatusSnapshot MergeSnapshots(
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
        // Merge child sessions as a union.
        var mergedChildren = existing.ChildSessions.Count == 0
            ? incoming.ChildSessions
            : incoming.ChildSessions.Count == 0
                ? existing.ChildSessions
                : existing.ChildSessions
                    .Union(incoming.ChildSessions, StringComparer.Ordinal)
                    .ToList()
                    .AsReadOnly();

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

    // ── Private JSON helpers ──────────────────────────────────────────────────

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var prop)
            && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static long? GetLong(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var prop)
            && prop.ValueKind == JsonValueKind.Number
            && prop.TryGetInt64(out var val))
            return val;
        return null;
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var prop)
            && prop.ValueKind == JsonValueKind.Number
            && prop.TryGetInt32(out var val))
            return val;
        return null;
    }

    private static bool? GetBool(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
        }
        return null;
    }

    private static decimal? GetDecimal(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var prop)
            && prop.ValueKind == JsonValueKind.Number
            && prop.TryGetDecimal(out var val))
            return val;
        return null;
    }

    private static bool TryGetObject(JsonElement element, string propertyName, out JsonElement result)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out result)
            && result.ValueKind == JsonValueKind.Object)
            return true;

        result = default;
        return false;
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var prop)
            && prop.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in prop.EnumerateArray())
            {
                var str = item.GetString();
                if (!string.IsNullOrEmpty(str))
                    list.Add(str);
            }
            return list.AsReadOnly();
        }
        return Array.Empty<string>();
    }
}
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
        return existing is null ? fresh : AgentStatusMerger.MergeSnapshots(existing, fresh);
    }

    // ── History message extraction ────────────────────────────────────────────

    /// <summary>
    /// Builds a partial <see cref="AgentStatusSnapshot"/> from a chat history
    /// message (which follows a different schema from gateway events).
    /// Chat messages carry model, provider, stopReason, timestamp, and usage
    /// (tokens + cost) — but NOT session-level metadata like status/phase.
    /// The tracker&#39;s <see cref="AgentStatusTracker.Update"/> will merge this
    /// with any existing snapshot for the session via <c>MergeSnapshots</c>.
    /// </summary>
    public static AgentStatusSnapshot? FromHistoryMessage(
        JsonElement msg, string sessionKey)
    {
        if (msg.ValueKind != JsonValueKind.Object)
            return null;

        if (string.IsNullOrEmpty(sessionKey))
            return null;

        // ── Top-level fields ───────────────────────────────────────────
        string? model = null;
        string? modelProvider = null;
        string? stopReason = null;
        long? timestamp = null;

        if (msg.TryGetProperty("model", out var mEl) && mEl.ValueKind == JsonValueKind.String)
            model = mEl.GetString();

        // Chat messages use "provider" but AgentStatusSnapshot uses ModelProvider
        if (msg.TryGetProperty("provider", out var pEl) && pEl.ValueKind == JsonValueKind.String)
            modelProvider = pEl.GetString();

        if (msg.TryGetProperty("stopReason", out var srEl) && srEl.ValueKind == JsonValueKind.String)
            stopReason = srEl.GetString();

        if (msg.TryGetProperty("timestamp", out var tsEl) && tsEl.ValueKind == JsonValueKind.Number)
        {
            if (tsEl.TryGetInt64(out var tVal)) timestamp = tVal;
        }

        // ── Nested usage ────────────────────────────────────────────────
        long? inputTokens = null;
        long? outputTokens = null;
        long? totalTokens = null;
        decimal? costUsd = null;

        if (msg.TryGetProperty("usage", out var usageEl) && usageEl.ValueKind == JsonValueKind.Object)
        {
            if (usageEl.TryGetProperty("input", out var inp) && inp.ValueKind == JsonValueKind.Number)
            {
                if (inp.TryGetInt64(out var v)) inputTokens = v;
            }

            if (usageEl.TryGetProperty("output", out var outp) && outp.ValueKind == JsonValueKind.Number)
            {
                if (outp.TryGetInt64(out var v)) outputTokens = v;
            }

            if (usageEl.TryGetProperty("totalTokens", out var tt) && tt.ValueKind == JsonValueKind.Number)
            {
                if (tt.TryGetInt64(out var v)) totalTokens = v;
            }

            // usage.cost.total
            if (usageEl.TryGetProperty("cost", out var costEl) && costEl.ValueKind == JsonValueKind.Object)
            {
                if (costEl.TryGetProperty("total", out var ct) && ct.ValueKind == JsonValueKind.Number)
                {
                    if (ct.TryGetDecimal(out var d)) costUsd = d;
                }
            }
        }

        return new AgentStatusSnapshot
        {
            SessionKey = sessionKey,
            Model = model,
            ModelProvider = modelProvider,
            StopReason = stopReason,
            UpdatedAt = timestamp,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = totalTokens,
            EstimatedCostUsd = costUsd,
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
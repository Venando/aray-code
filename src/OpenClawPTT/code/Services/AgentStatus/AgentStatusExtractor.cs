using System.Text.Json;

namespace OpenClawPTT.Services;

/// <summary>
/// Extracts agent/subagent status snapshots from gateway event payloads.
/// Gathers every available field from the payload for future use.
/// </summary>
public static class AgentStatusExtractor
{
    /// <summary>
    /// Attempts to build a snapshot from any gateway event payload.
    /// Returns null when no sessionKey is present.
    /// </summary>
    public static AgentStatusSnapshot? Extract(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return null;

        // Top-level sessionKey
        if (!payload.TryGetProperty("sessionKey", out var sessionKeyEl))
            return null;

        var sessionKey = sessionKeyEl.GetString();
        if (string.IsNullOrEmpty(sessionKey))
            return null;

        // Determine parentSessionKey (top-level or nested in session)
        string? parentSessionKey = null;
        if (payload.TryGetProperty("parentSessionKey", out var parentEl))
            parentSessionKey = parentEl.GetString();

        // Try to get the nested session object
        JsonElement session = default;
        bool hasSession = payload.TryGetProperty("session", out session);

        // Try to get the nested message object (for stopReason)
        JsonElement message = default;
        bool hasMessage = payload.TryGetProperty("message", out message);

        var snapshot = new AgentStatusSnapshot
        {
            SessionKey = sessionKey,
            ParentSessionKey = parentSessionKey ?? GetString(session, "parentSessionKey") ?? GetString(session, "spawnedBy"),
            DisplayName = GetString(session, "displayName") ?? GetString(payload, "displayName"),
            Status = GetString(session, "status"),
            StopReason = GetString(message, "stopReason"),
            Model = GetString(session, "model"),
            ModelProvider = GetString(session, "modelProvider"),
            InputTokens = GetLong(session, "inputTokens"),
            OutputTokens = GetLong(session, "outputTokens"),
            TotalTokens = GetLong(session, "totalTokens"),
            StartedAt = GetLong(session, "startedAt") ?? GetLong(payload, "startedAt") ?? GetLong(payload, "ts"),
            EndedAt = GetLong(session, "endedAt"),
            RuntimeMs = GetLong(session, "runtimeMs"),
            SubagentRunState = GetString(session, "subagentRunState"),
            HasActiveSubagentRun = GetBool(session, "hasActiveSubagentRun"),
            SubagentRole = GetString(session, "subagentRole"),
            SpawnDepth = GetInt(session, "spawnDepth"),
            ContextTokens = GetLong(session, "contextTokens"),
            LastChannel = GetString(session, "lastChannel"),
            AbortedLastRun = GetBool(session, "abortedLastRun"),
            UpdatedAt = GetLong(session, "updatedAt") ?? GetLong(payload, "updatedAt") ?? GetLong(payload, "ts"),
            ChildSessions = GetStringArray(session, "childSessions")
        };

        return snapshot;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var prop))
            return prop.GetString();
        return null;
    }

    private static long? GetLong(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number)
            {
                if (prop.TryGetInt64(out var val))
                    return val;
            }
        }
        return null;
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number)
            {
                if (prop.TryGetInt32(out var val))
                    return val;
            }
        }
        return null;
    }

    private static bool? GetBool(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
        }
        return null;
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Array)
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
        }
        return Array.Empty<string>();
    }
}

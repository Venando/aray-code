using System.Text.Json;

namespace ArayCode;

/// <summary>
/// Event raised when the gateway falls back from one model provider to another.
/// Contains the original provider/model that failed, the fallback target, and the raw error message.
/// Payload field names match the DiagnosticFailoverEvent shape (fromProvider/fromModel/toProvider/toModel/reason).
/// </summary>
public record ModelFallbackEvent(
    string EventName,
    JsonElement Payload)
{
    /// <summary>The provider that failed.</summary>
    public string? FailedProvider => TryGet("fromProvider");

    /// <summary>The model that failed.</summary>
    public string? FailedModel => TryGet("fromModel");

    /// <summary>The provider the fallback switched to.</summary>
    public string? FallbackProvider => TryGet("toProvider");
    
    /// <summary>The model the fallback switched to.</summary>
    public string? FallbackModel => TryGet("toModel");

    /// <summary>The reason for the fallback (e.g. "rate_limit", "timeout").</summary>
    public string? ErrorMessage => TryGet("reason");

    /// <summary>Whether this fallback ultimately succeeded (model.failover events are only sent when fallback occurs).</summary>
    public bool Succeeded => true;

    private string? TryGet(string key)
    {
        if (Payload.ValueKind != JsonValueKind.Object) return null;
        if (!Payload.TryGetProperty(key, out var el)) return null;
        return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }
}

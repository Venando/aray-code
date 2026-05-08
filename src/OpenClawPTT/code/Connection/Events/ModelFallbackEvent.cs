using System.Text.Json;

namespace OpenClawPTT;

/// <summary>
/// Event raised when the gateway falls back from one model provider to another.
/// Contains the original provider/model that failed, the fallback target, and the raw error message.
/// </summary>
public record ModelFallbackEvent(
    string EventName,
    JsonElement Payload)
{
    /// <summary>The provider that failed (e.g. "kimi").</summary>
    public string? FailedProvider => TryGet("candidateProvider");

    /// <summary>The model that failed (e.g. "kimi-k2.6").</summary>
    public string? FailedModel => TryGet("candidateModel");

    /// <summary>The provider the fallback switched to (e.g. "deepseek").</summary>
    public string? FallbackProvider => TryGet("nextCandidateProvider");
    
    /// <summary>The model the fallback switched to (e.g. "deepseek-v4-flash").</summary>
    public string? FallbackModel => TryGet("nextCandidateModel");

    /// <summary>The raw error message from the failed provider.</summary>
    public string? ErrorMessage => TryGet("errorPreview");

    /// <summary>The decision made: "candidate_failed", "candidate_succeeded", etc.</summary>
    public string? Decision => TryGet("decision");

    /// <summary>Whether this fallback ultimately succeeded.</summary>
    public bool Succeeded => Decision == "candidate_succeeded";

    /// <summary>Whether the error was a quota/limit issue (403/429/401).</summary>
    public bool IsQuotaError
    {
        get
        {
            var msg = ErrorMessage ?? string.Empty;
            return msg.Contains("usage limit", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("quota", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("billing", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("insufficient", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("exhausted", StringComparison.OrdinalIgnoreCase);
        }
    }

    private string? TryGet(string key)
    {
        if (Payload.ValueKind != JsonValueKind.Object) return null;
        return Payload.TryGetProperty(key, out var el) ? el.GetString() : null;
    }
}

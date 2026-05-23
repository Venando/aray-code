using System;
using System.Text.Json;

namespace ArayCode;

public sealed class GatewayException : Exception
{
    public JsonElement? Raw { get; }
    public string? DetailCode { get; }
    public string? RecommendedStep { get; }

    public GatewayException(string message, JsonElement? raw = null) : base(message)
    {
        Raw = raw;
        if (raw?.ValueKind == JsonValueKind.Object)
        {
            if (raw.Value.TryGetProperty("error", out var err)
                && err.TryGetProperty("details", out var det))
            {
                DetailCode = det.TryGetProperty("code", out var c) ? c.GetString() : null;
                RecommendedStep = det.TryGetProperty("recommendedNextStep", out var r) ? r.GetString() : null;
            }
        }
    }
}
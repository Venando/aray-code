using System;
using System.Collections.Generic;
using System.Text.Json;

namespace OpenClawPTT.Services.Diagnostics;

/// <summary>Category of a gateway error.</summary>
public enum ErrorCategory
{
    /// <summary>Network issue — safe to retry automatically.</summary>
    Transient,
    /// <summary>Auth, config, or pairing issue — user must take action.</summary>
    Actionable,
    /// <summary>Cannot proceed — app should exit gracefully.</summary>
    Fatal
}

/// <summary>Result of classifying a gateway error.</summary>
public sealed class ErrorClassification
{
    public ErrorCategory Category { get; set; }
    public string Code { get; set; } = string.Empty;
    public string HumanMessage { get; set; } = string.Empty;
    public string[] SuggestedActions { get; set; } = Array.Empty<string>();
    public bool ShouldRetry { get; set; }
    public bool ShouldStopApp { get; set; }
    public string OuterCode { get; set; } = string.Empty;
    public string? RawMessage { get; set; }
    public string? StackTrace { get; set; }
    public string? Reason { get; set; }
    public string? RequestId { get; set; }
    public string? DeviceId { get; set; }
    public string? RequestedRole { get; set; }
    public string[]? RequestedScopes { get; set; }
    public string[]? ApprovedScopes { get; set; }
    public string[]? ApprovedRoles { get; set; }
    public string? Method { get; set; }
    public bool? CanRetryWithDeviceToken { get; set; }
    public string? RecommendedNextStep { get; set; }
    public int? RetryAfterMs { get; set; }

    public ErrorLogEntry ToLogEntry(string level = "error", string category = "gateway", int? retryAttempt = null)
    {
        return new ErrorLogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Category = category,
            Code = Code,
            OuterCode = OuterCode,
            Message = HumanMessage,
            SuggestedActions = SuggestedActions,
            RetryAttempt = retryAttempt,
            RawException = RawMessage,
            StackTrace = StackTrace,
            Reason = Reason,
            RequestId = RequestId,
            DeviceId = DeviceId,
            RequestedRole = RequestedRole,
            RequestedScopes = RequestedScopes,
            ApprovedScopes = ApprovedScopes,
            ApprovedRoles = ApprovedRoles,
            Method = Method,
            CanRetryWithDeviceToken = CanRetryWithDeviceToken,
            RecommendedNextStep = RecommendedNextStep,
            RetryAfterMs = RetryAfterMs
        };
    }
}

/// <summary>Classifies GatewayException and connection errors into actionable categories.</summary>
public static class GatewayErrorClassifier
{
    /// <summary>Classify a raw exception (network-level failures).</summary>
    public static ErrorClassification Classify(Exception ex)
    {
        if (ex is GatewayException gex)
            return ClassifyGatewayError(gex);

        var message = ex.Message ?? string.Empty;

        // Explicit timeout — usually means the server/gateway is unreachable or the
        // handshake got stuck (e.g. DNS, TCP, or waiting for connect.challenge).
        if (ex is TimeoutException)
        {
            return new ErrorClassification
            {
                Category = ErrorCategory.Transient,
                Code = "CONNECT_TIMEOUT",
                HumanMessage = $"Connection timed out: {message}",
                ShouldRetry = true,
                RawMessage = message,
                StackTrace = ex.StackTrace
            };
        }

        // Network-level failures are transient
        if (ex is System.Net.WebSockets.WebSocketException ||
            ex is System.IO.IOException ||
            message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("refused", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("reset", StringComparison.OrdinalIgnoreCase))
        {
            return new ErrorClassification
            {
                Category = ErrorCategory.Transient,
                Code = "NETWORK_ERROR",
                HumanMessage = $"Network error: {message}",
                ShouldRetry = true,
                RawMessage = message,
                StackTrace = ex.StackTrace
            };
        }

        return new ErrorClassification
        {
            Category = ErrorCategory.Fatal,
            Code = "UNKNOWN_ERROR",
            HumanMessage = $"Unexpected error: {message}",
            ShouldRetry = false,
            ShouldStopApp = true,
            RawMessage = message,
            StackTrace = ex.StackTrace
        };
    }

    /// <summary>Classify a structured GatewayException from the gateway.</summary>
    public static ErrorClassification ClassifyGatewayError(GatewayException ex)
    {
        var code = ex.DetailCode ?? string.Empty;
        var message = ex.Message ?? string.Empty;
        var recommended = ex.RecommendedStep;
        var rawPayload = ex.Raw;

        // Extract more detail from the JSON payload
        string? outerCode = null;
        string? reason = null;
        string? remediationHint = null;
        string[]? requestedScopes = null;
        string[]? approvedScopes = null;

        if (rawPayload?.ValueKind == JsonValueKind.Object)
        {
            if (rawPayload.Value.TryGetProperty("error", out var err))
            {
                if (err.TryGetProperty("code", out var oc))
                    outerCode = oc.GetString();

                if (err.TryGetProperty("details", out var det))
                {
                    if (det.TryGetProperty("reason", out var r))
                        reason = r.GetString();
                    if (det.TryGetProperty("remediationHint", out var rh))
                        remediationHint = rh.GetString();
                    if (det.TryGetProperty("requestedScopes", out var rs) && rs.ValueKind == JsonValueKind.Array)
                        requestedScopes = JsonSerializer.Deserialize<string[]>(rs.GetRawText());
                    if (det.TryGetProperty("approvedScopes", out var as_) && as_.ValueKind == JsonValueKind.Array)
                        approvedScopes = JsonSerializer.Deserialize<string[]>(as_.GetRawText());
                }
            }
        }

        // ── Actionable errors that need user intervention ──

        if (string.Equals(code, "AUTH_DEVICE_TOKEN_MISMATCH", StringComparison.OrdinalIgnoreCase))
        {
            var actions = new List<string>
            {
                "Clear DeviceToken from config and restart the app.",
                "Or run: openclaw device token rotate"
            };
            if (!string.IsNullOrEmpty(recommended))
                actions.Add($"Recommended: {recommended}");

            return new ErrorClassification
            {
                Category = ErrorCategory.Actionable,
                Code = code,
                HumanMessage = "Device token has changed — the gateway no longer recognizes this device.",
                SuggestedActions = actions.ToArray(),
                ShouldRetry = false,
                RawMessage = message,
                StackTrace = ex.StackTrace
            };
        }

        if (string.Equals(code, "PAIRING_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("pairing required", StringComparison.OrdinalIgnoreCase))
        {
            var actions = new List<string>
            {
                "Run 'openclaw devices list' to see pending requests.",
                "Then run 'openclaw devices approve <request-id>' to approve."
            };

            if (reason == "scope-upgrade")
            {
                actions.Insert(0, "Device needs more permissions (scope upgrade).");
                if (requestedScopes != null)
                    actions.Add($"Requested scopes: {string.Join(", ", requestedScopes)}");
            }

            if (!string.IsNullOrEmpty(remediationHint))
                actions.Add($"Hint: {remediationHint}");

            if (!string.IsNullOrEmpty(recommended))
                actions.Add($"Recommended: {recommended}");

            return new ErrorClassification
            {
                Category = ErrorCategory.Actionable,
                Code = code,
                HumanMessage = reason == "scope-upgrade"
                    ? "Pairing required: device is asking for more scopes than currently approved."
                    : "Pairing required: device is not paired with the gateway.",
                SuggestedActions = actions.ToArray(),
                ShouldRetry = false,
                RawMessage = message,
                StackTrace = ex.StackTrace
            };
        }

        if (message.Contains("missing scope", StringComparison.OrdinalIgnoreCase))
        {
            return new ErrorClassification
            {
                Category = ErrorCategory.Actionable,
                Code = "MISSING_SCOPE",
                HumanMessage = "The device is missing a required permission scope.",
                SuggestedActions = new[]
                {
                    "Approve the pending device pairing request to upgrade scopes.",
                    "Run 'openclaw devices list' to check pending requests.",
                    "Then run 'openclaw devices approve <request-id>'."
                },
                ShouldRetry = false,
                RawMessage = message,
                StackTrace = ex.StackTrace
            };
        }

        if (string.Equals(code, "UNAUTHORIZED", StringComparison.OrdinalIgnoreCase) ||
            (message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(code, "AUTH_DEVICE_TOKEN_MISMATCH", StringComparison.OrdinalIgnoreCase)))
        {
            return new ErrorClassification
            {
                Category = ErrorCategory.Actionable,
                Code = "UNAUTHORIZED",
                HumanMessage = "Authentication failed. Check your gateway credentials.",
                SuggestedActions = new[]
                {
                    "Verify the gateway token in your config.",
                    "Run 'openclaw gateway status' to check gateway health."
                },
                ShouldRetry = false,
                RawMessage = message,
                StackTrace = ex.StackTrace
            };
        }

        // ── Transient errors (network, timeout) ──

        if (message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("reset", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("refused", StringComparison.OrdinalIgnoreCase))
        {
            return new ErrorClassification
            {
                Category = ErrorCategory.Transient,
                Code = code,
                HumanMessage = $"Connection issue: {message}",
                ShouldRetry = true,
                RawMessage = message,
                StackTrace = ex.StackTrace
            };
        }

        // ── Unknown/fallback — treat as actionable with guidance ──
        return new ErrorClassification
        {
            Category = ErrorCategory.Actionable,
            Code = code,
            HumanMessage = $"Gateway error: {message}",
            SuggestedActions = new[]
            {
                "Check gateway logs: openclaw logs --follow",
                "Check gateway status: openclaw gateway status"
            },
            ShouldRetry = false,
            RawMessage = message,
            StackTrace = ex.StackTrace
        };
    }

    /// <summary>Determines if an error message indicates a quota/limit exceeded condition.</summary>
    public static bool IsQuotaError(string message)
    {
        return message.Contains("usage limit", StringComparison.OrdinalIgnoreCase)
            || message.Contains("quota", StringComparison.OrdinalIgnoreCase)
            || message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || message.Contains("billing", StringComparison.OrdinalIgnoreCase)
            || message.Contains("insufficient funds", StringComparison.OrdinalIgnoreCase)
            || message.Contains("insufficient balance", StringComparison.OrdinalIgnoreCase)
            || message.Contains("billing cycle", StringComparison.OrdinalIgnoreCase)
            || message.Contains("billing error", StringComparison.OrdinalIgnoreCase)
            || message.Contains("exhausted", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Determines if an error message indicates a rate limit condition.</summary>
    public static bool IsRateLimitError(string message)
    {
        return message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || message.Contains("too many requests", StringComparison.OrdinalIgnoreCase)
            || message.Contains("429", StringComparison.OrdinalIgnoreCase)
            || message.Contains("too many concurrent", StringComparison.OrdinalIgnoreCase);
    }
}

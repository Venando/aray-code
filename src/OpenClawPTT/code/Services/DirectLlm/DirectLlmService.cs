using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using OpenClawPTT.Services.DirectLlm.Models;

namespace OpenClawPTT.Services;

/// <summary>
/// Service for sending messages directly to an LLM bypassing the OpenClaw agent.
/// Supports OpenAI-compatible and Anthropic API formats.
/// </summary>
public interface IDirectLlmService : IDisposable
{
    /// <summary>
    /// Send a message to the configured LLM and return the response text.
    /// </summary>
    Task<string> SendAsync(string message, CancellationToken ct = default);

    /// <summary>
    /// Probes the configured LLM endpoint for reachability.
    /// Returns true if the endpoint responds successfully, false otherwise.
    /// Does not send a user message — just a minimal connectivity check.
    /// </summary>
    Task<bool> ProbeAsync(CancellationToken ct = default);

    bool IsConfigured { get; }

    /// <summary>
    /// Failure tracker that records send successes/failures for status monitoring.
    /// Returns null if not wired (e.g. mock implementations).
    /// </summary>
    IDirectLlmFailureTracker? FailureTracker { get; }

    /// <summary>
    /// Updates the service's configuration at runtime.
    /// The service will use the new URL/model/token/api type for subsequent calls.
    /// </summary>
    void UpdateConfig(AppConfig config);
}

/// <summary>
/// Implementation of direct LLM service supporting OpenAI and Anthropic APIs.
/// Includes retry with exponential backoff for transient failures.
/// </summary>
public sealed class DirectLlmService : IDirectLlmService, IDisposable
{
    // Static handler with connection pooling — shared across all DirectLlmService instances.
    // Prevents socket exhaustion when services are created/destroyed on config changes.
    private static readonly SocketsHttpHandler SharedHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
        MaxConnectionsPerServer = 5
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private AppConfig _config;
    private readonly IDirectLlmFailureTracker _failureTracker;
    private bool _disposed;
    private readonly object _configLock = new();

    /// <summary>Maximum retry attempts for transient failures.</summary>
    internal const int MaxRetries = 1;

    /// <summary>Base delay in ms for exponential backoff.</summary>
    internal const int RetryBaseDelayMs = 500;
    internal const int ProbeTimeoutSeconds = 30;
    internal const int SendTimeoutMinutes = 5;

    public DirectLlmService(AppConfig config, IDirectLlmFailureTracker? failureTracker = null, HttpMessageHandler? handler = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _failureTracker = failureTracker ?? new DirectLlmFailureTracker();

        if (handler != null)
        {
            // Test-provided handler — own it for lifecycle management
            _httpClient = new HttpClient(handler, disposeHandler: true);
            _ownsClient = true;
        }
        else
        {
            // Production: use shared connection-pooled handler, don't own it
            _httpClient = new HttpClient(SharedHandler, disposeHandler: false);
            _ownsClient = false;
        }

        // Set timeout for local LLMs (Ollama may be slower on first request)
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    public bool IsConfigured
    {
        get
        {
            var cfg = GetConfig();
            return !string.IsNullOrWhiteSpace(cfg.DirectLlmUrl) &&
                   !string.IsNullOrWhiteSpace(cfg.DirectLlmModelName);
        }
    }

    public IDirectLlmFailureTracker FailureTracker => _failureTracker;

    public void UpdateConfig(AppConfig config)
    {
        lock (_configLock)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }
    }

    /// <summary>
    /// Sends a message to the configured LLM with retry on transient failures.
    /// Records success/failure to the tracker on final outcome.
    /// </summary>
    public async Task<string> SendAsync(string message, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DirectLlmService));
        if (!IsConfigured) throw new InvalidOperationException("Direct LLM is not configured. Set DirectLlmUrl and DirectLlmModelName in config.");

        return await SendWithRetryAsync(message, ct);
    }

    /// <summary>
    /// Executes the send with retry logic.
    /// Retries only on transient failures (network errors, timeouts, 5xx).
    /// Does NOT retry on cancellation or 4xx client errors.
    /// </summary>
    private async Task<string> SendWithRetryAsync(string message, CancellationToken ct)
    {
        var cfg = GetConfig();
        int attempt = 0;

        while (true)
        {
            try
            {
                var apiType = NormalizeApiType(cfg.DirectLlmApiType);

                string result = apiType switch
                {
                    "anthropic-messages" => await SendAnthropicAsync(message, ct),
                    _ => await SendOpenAiAsync(message, ct)
                };

                _failureTracker.RecordSuccess();
                return result;
            }
            catch (Exception ex) when (attempt < MaxRetries && IsRetryable(ex))
            {
                attempt++;
                var delay = ComputeBackoffMs(attempt);
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // User-requested cancellation — don't record as a failure
                throw;
            }
            catch (Exception)
            {
                // All other failures including timeout (TaskCanceledException without
                // user cancellation) and non-retryable errors
                // Note: TaskCanceledException inherits from OperationCanceledException
                // but without user cancellation it falls through to here
                _failureTracker.RecordFailure();
                throw;
            }
        }
    }

    /// <summary>
    /// Determines if an exception is retryable.
    /// Retry on: HttpRequestException (network), TaskCanceledException (timeout), 5xx.
    /// Do NOT retry on: OperationCanceledException (user cancellation).
    /// </summary>
    private static bool IsRetryable(Exception ex)
    {
        // HttpRequestException covers network errors and non-success status codes
        if (ex is HttpRequestException httpEx)
        {
            // Check for 5xx server errors — retry those
            if (httpEx.StatusCode.HasValue)
            {
                int code = (int)httpEx.StatusCode.Value;
                return code >= 500 && code <= 599;
            }
            // No status code = network-level error (connection refused, DNS, etc.)
            return true;
        }

        // TaskCanceledException typically means HttpClient timeout
        if (ex is TaskCanceledException)
            return true;

        return false;
    }

    /// <summary>
    /// Computes exponential backoff delay: baseDelay * 2^(attempt-1) with random jitter.
    /// Capped at 4000ms.
    /// </summary>
    internal static int ComputeBackoffMs(int attempt)
    {
        int delay = RetryBaseDelayMs * (1 << (attempt - 1)); // 500, 1000, 2000, ...
        delay = Math.Min(delay, 4000);

        // Add jitter: ±50%
        var rng = new Random();
        double jitter = 1.0 + (rng.NextDouble() - 0.5); // 0.5 to 1.5
        return (int)(delay * jitter);
    }

    /// <summary>Thread-safe read of the current config reference.</summary>
    private AppConfig GetConfig()
    {
        lock (_configLock) return _config;
    }

    private static string NormalizeApiType(string? apiType)
    {
        var normalized = apiType?.ToLowerInvariant() ?? "openai-completions";
        // openai-chat is an alias for openai-completions
        if (normalized == "openai-chat")
            normalized = "openai-completions";
        return normalized;
    }

    public async Task<bool> ProbeAsync(CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DirectLlmService));
        if (!IsConfigured) return false;

        try
        {
            var cfg = GetConfig();
            var apiType = NormalizeApiType(cfg.DirectLlmApiType);

            return apiType switch
            {
                "anthropic-messages" => await ProbeAnthropicAsync(ct),
                _ => await ProbeOpenAiAsync(ct),
            };
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Probes an OpenAI-compatible endpoint with a minimal request (max_tokens=1).
    /// </summary>
    private async Task<bool> ProbeOpenAiAsync(CancellationToken ct)
    {
        var requestBody = new OpenAiRequest
        {
            Model = _config.DirectLlmModelName!,
            Messages = new[]
            {
                new OpenAiMessage { Role = "user", Content = "hi" }
            },
            MaxTokens = 1,
            Stream = false
        };

        var url = BuildOpenAiUrl(_config.DirectLlmUrl!);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(ProbeTimeoutSeconds));

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(requestBody, options: new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            })
        };

        if (!string.IsNullOrWhiteSpace(_config.DirectLlmToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.DirectLlmToken);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Probes an Anthropic endpoint with a minimal request (max_tokens=1).
    /// </summary>
    private async Task<bool> ProbeAnthropicAsync(CancellationToken ct)
    {
        var requestBody = new AnthropicRequest
        {
            Model = _config.DirectLlmModelName!,
            Messages = new[]
            {
                new AnthropicMessage { Role = "user", Content = "hi" }
            },
            MaxTokens = 1,
            Stream = false
        };

        var url = BuildAnthropicUrl(_config.DirectLlmUrl!);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(ProbeTimeoutSeconds));

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(requestBody, options: new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            })
        };

        request.Headers.Add("x-api-key", _config.DirectLlmToken ?? string.Empty);
        request.Headers.Add("anthropic-version", "2023-06-01");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        return response.IsSuccessStatusCode;
    }

    private async Task<string> SendOpenAiAsync(string message, CancellationToken ct)
    {
        var requestBody = new OpenAiRequest
        {
            Model = _config.DirectLlmModelName!,
            Messages = new[]
            {
                new OpenAiMessage { Role = "user", Content = message }
            },
            Stream = false
        };

        // Build OpenAI URL: host → /v1/chat/completions, /v1 → /v1/chat/completions
        var url = BuildOpenAiUrl(_config.DirectLlmUrl!);

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(requestBody, options: new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            })
        };

        if (!string.IsNullOrWhiteSpace(_config.DirectLlmToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.DirectLlmToken);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(SendTimeoutMinutes));
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadFromJsonAsync<OpenAiResponse>(timeoutCts.Token);
        return responseJson?.Choices?.FirstOrDefault()?.Message?.Content?.Trim() ?? "(No response)";
    }

    private async Task<string> SendAnthropicAsync(string message, CancellationToken ct)
    {
        var requestBody = new AnthropicRequest
        {
            Model = _config.DirectLlmModelName!,
            Messages = new[]
            {
                new AnthropicMessage { Role = "user", Content = message }
            },
            MaxTokens = _config.DirectLlmMaxTokens,
            Stream = false
        };

        // Build Anthropic URL: host → /v1/messages, /v1 → /v1/messages
        var url = BuildAnthropicUrl(_config.DirectLlmUrl!);

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(requestBody, options: new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            })
        };

        request.Headers.Add("x-api-key", _config.DirectLlmToken ?? string.Empty);
        request.Headers.Add("anthropic-version", "2023-06-01");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(SendTimeoutMinutes));
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadFromJsonAsync<AnthropicResponse>(timeoutCts.Token);
        // Aggregate all "text" type content blocks, skip "thinking" and others
        var textParts = responseJson?.Content
            ?.Where(c => c.Type == "text")
            .Select(c => c.Text)
            .ToList();
        return textParts?.Count > 0
            ? string.Join("\n", textParts).Trim()
            : "(No response)";
    }

    /// <summary>
    /// Builds a complete OpenAI API URL from a partial URL.
    /// - host → host/v1/chat/completions
    /// - /v1 → /v1/chat/completions
    /// - /v1/chat/completions (unchanged)
    /// </summary>
    private static string BuildOpenAiUrl(string url)
    {
        if (url.EndsWith("/v1/chat/completions", StringComparison.OrdinalIgnoreCase))
            return url;

        if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ||
            url.EndsWith("/v1/", StringComparison.OrdinalIgnoreCase))
        {
            return url.TrimEnd('/') + "/chat/completions";
        }

        // Base URL (just host) - append full path
        return url.TrimEnd('/') + "/v1/chat/completions";
    }

    /// <summary>
    /// Builds a complete Anthropic API URL from a partial URL.
    /// - host → host/v1/messages
    /// - /v1 → /v1/messages
    /// - /v1/messages (unchanged)
    /// </summary>
    private static string BuildAnthropicUrl(string url)
    {
        if (url.EndsWith("/v1/messages", StringComparison.OrdinalIgnoreCase))
            return url;

        if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ||
            url.EndsWith("/v1/", StringComparison.OrdinalIgnoreCase))
        {
            return url.TrimEnd('/') + "/messages";
        }

        // Base URL (just host) - append full path
        return url.TrimEnd('/') + "/v1/messages";
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_ownsClient)
                _httpClient.Dispose();
            _disposed = true;
        }
    }


}

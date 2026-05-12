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
}

/// <summary>
/// Implementation of direct LLM service supporting OpenAI and Anthropic APIs.
/// </summary>
public sealed class DirectLlmService : IDirectLlmService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly AppConfig _config;
    private bool _disposed;

    public DirectLlmService(AppConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _httpClient = new HttpClient();
        
        // Set timeout for local LLMs (Ollama may be slower on first request)
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
    }

    public bool IsConfigured => 
        !string.IsNullOrWhiteSpace(_config.DirectLlmUrl) &&
        !string.IsNullOrWhiteSpace(_config.DirectLlmModelName);

    public async Task<string> SendAsync(string message, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DirectLlmService));
        if (!IsConfigured) throw new InvalidOperationException("Direct LLM is not configured. Set DirectLlmUrl and DirectLlmModelName in config.");

        var apiType = _config.DirectLlmApiType?.ToLowerInvariant() ?? "openai-completions";
        
        // openai-chat is an alias for openai-completions
        if (apiType == "openai-chat")
            apiType = "openai-completions";

        return apiType switch
        {
            "anthropic-messages" => await SendAnthropicAsync(message, ct),
            _ => await SendOpenAiAsync(message, ct) // default to openai-completions
        };
    }

    public async Task<bool> ProbeAsync(CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DirectLlmService));
        if (!IsConfigured) return false;

        try
        {
            var apiType = _config.DirectLlmApiType?.ToLowerInvariant() ?? "openai-completions";
            if (apiType == "openai-chat")
                apiType = "openai-completions";

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
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));

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
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));

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

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadFromJsonAsync<OpenAiResponse>(ct);
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
            MaxTokens = 4096,
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

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadFromJsonAsync<AnthropicResponse>(ct);
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
            _httpClient.Dispose();
            _disposed = true;
        }
    }


}

using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ArayCode.Transcriber;

/// <summary>
/// Adapter for OpenAI Whisper API transcription.
/// </summary>
public sealed class OpenAiTranscriberAdapter : ITranscriber
{
    private const string OpenAiApiUrl = "https://api.openai.com/v1/audio/transcriptions";
    private const string DefaultModel = "whisper-1";
    private const int DefaultMaxRetries = 3;
    private const int DefaultTimeoutSeconds = 30;
    private const long DefaultMaxAudioSizeBytes = 10 * 1024 * 1024; // 10 MB

    private readonly HttpClient _http;
    private readonly string _model;
    private readonly int _maxRetries;
    private readonly long _maxAudioSizeBytes;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Creates an adapter with a real HttpClient using the default handler chain.
    /// </summary>
    public OpenAiTranscriberAdapter(string apiKey, string model = DefaultModel)
        : this(apiKey, model, DefaultMaxRetries, DefaultTimeoutSeconds, DefaultMaxAudioSizeBytes,
               new HttpClientHandler())
    {
    }

    /// <summary>
    /// Internal constructor allowing injection of a custom HttpMessageHandler for testing.
    /// </summary>
    internal OpenAiTranscriberAdapter(
        string apiKey,
        string model,
        int maxRetries,
        int timeoutSeconds,
        long maxAudioSizeBytes,
        HttpMessageHandler httpHandler)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentNullException(nameof(apiKey), "OpenAI API key must not be null or empty.");

        if (maxRetries < 0)
            throw new ArgumentOutOfRangeException(nameof(maxRetries), "Max retries must be non-negative.");
        if (timeoutSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "Timeout must be positive.");
        if (maxAudioSizeBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxAudioSizeBytes), "Max audio size must be positive.");

        _model = model ?? DefaultModel;
        _maxRetries = maxRetries;
        _maxAudioSizeBytes = maxAudioSizeBytes;

        _http = new HttpClient(httpHandler)
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<string> TranscribeAsync(byte[] wavBytes, string fileName = "audio.wav", CancellationToken ct = default)
    {
        // Guard: audio must not be null or empty
        if (wavBytes == null || wavBytes.Length == 0)
            throw new ArgumentNullException(nameof(wavBytes), "WAV bytes must not be null or empty.");

        // Guard: enforce audio size limit
        if (wavBytes.Length > _maxAudioSizeBytes)
            throw new ArgumentOutOfRangeException(
                nameof(wavBytes),
                $"Audio data exceeds maximum allowed size of {_maxAudioSizeBytes} bytes ({wavBytes.Length} bytes provided).");

        // Ensure only one transcription runs at a time
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await TranscribeCoreAsync(wavBytes, fileName, ct).ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<string> TranscribeCoreAsync(byte[] wavBytes, string fileName, CancellationToken ct)
    {
        Exception? lastException = null;

        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // Each attempt gets a fresh MemoryStream so position resets to 0
                return await SendTranscriptionRequestAsync(wavBytes, fileName, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout — treat as transient, retry
                lastException = new TranscriberException("OpenAI API request timed out.", lastException!);
            }
            catch (HttpRequestException ex)
            {
                // Network-level or HTTP error (e.g. 503) — treat as transient, retry
                lastException = ex;
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                // Explicitly cancelled due to HttpClient timeout
                lastException = new TranscriberException("OpenAI API request timed out.", lastException!);
            }
            catch (TranscriberException ex) when (ex.InnerException is HttpRequestException)
            {
                // Wrapped HTTP error (e.g. 503) from SendTranscriptionRequestAsync — treat as transient, retry
                lastException = ex.InnerException;
            }

            if (attempt < _maxRetries)
            {
                // Exponential backoff: 1s, 2s, 4s, ...
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }

        // All retries exhausted — wrap in TranscriberException for callers expecting a consistent type
        if (lastException is HttpRequestException httpEx)
            throw new TranscriberException(httpEx.Message, httpEx);
        throw lastException!;
    }

    private async Task<string> SendTranscriptionRequestAsync(byte[] wavBytes, string fileName, CancellationToken ct)
    {
        using var audioStream = new MemoryStream(wavBytes);
        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(audioStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

        content.Add(fileContent, "file", fileName);
        content.Add(new StringContent(_model), "model");
        content.Add(new StringContent("text"), "response_format");

        var response = await _http.PostAsync(OpenAiApiUrl, content, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        // Fail immediately on 401 and 400 — never retry client errors
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new TranscriberException($"OpenAI API returned HTTP {(int)response.StatusCode} (Unauthorized): {body}");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            throw new TranscriberException($"OpenAI API returned HTTP {(int)response.StatusCode} (Bad Request): {body}");
        }

        // All other non-success codes (including 503 Service Unavailable) are treated
        // as transient — throw HttpRequestException so the retry loop catches them
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"OpenAI API returned HTTP {(int)response.StatusCode}: {body}");
        }

        return ExtractText(body);
    }

    private static string ExtractText(string body)
    {
        body = body.Trim();

        if (body.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("text", out var textProp))
                    return textProp.GetString()?.Trim() ?? string.Empty;
            }
            catch (JsonException)
            {
                // Fall through — return raw body
            }
        }

        return body;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _http.Dispose();
            _semaphore.Dispose();
            _disposed = true;
        }
    }
}

using System.Net.Http.Headers;
using System.Text.Json;

/// <summary>
/// A self-contained module for transcribing audio using the Groq Whisper API.
/// Initialize with your Groq API key, then call TranscribeAsync() with raw WAV bytes.
/// </summary>
public sealed class GroqTranscriber : IDisposable
{
    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------
    private const string GroqApiUrl = "https://api.groq.com/openai/v1/audio/transcriptions";
    private const string DefaultModel = "whisper-large-v3-turbo";

    // -----------------------------------------------------------------------
    // Fields
    // -----------------------------------------------------------------------
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly int _retryCount;
    private readonly int _retryDelayMs;
    private readonly double _retryBackoffFactor;
    private bool _disposed;

    // -----------------------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a new <see cref="GroqTranscriber"/> instance.
    /// </summary>
    /// <param name="apiKey">
    ///   Your Groq API key (e.g. "gsk_…").
    ///   Throws <see cref="ArgumentNullException"/> when null or whitespace.
    /// </param>
    /// <param name="model">
    ///   Whisper model to use. Defaults to <c>whisper-large-v3-turbo</c>.
    /// </param>
    /// <param name="retryCount">
    ///   Number of retry attempts on transient failures. Default 0 (no retry).
    /// </param>
    /// <param name="retryDelayMs">
    ///   Base delay between retries in milliseconds. Default 1000.
    /// </param>
    /// <param name="retryBackoffFactor">
    ///   Multiplier applied to delay after each retry. Default 2.0 (exponential backoff).
    /// </param>
    public GroqTranscriber(string apiKey, string model = DefaultModel, int retryCount = 0, int retryDelayMs = 1000, double retryBackoffFactor = 2.0)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentNullException(nameof(apiKey), "Groq API key must not be null or empty.");

        _model = model ?? DefaultModel;
        _retryCount = retryCount;
        _retryDelayMs = retryDelayMs;
        _retryBackoffFactor = retryBackoffFactor;

        _http = new HttpClient();
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Transcribes the supplied WAV audio bytes using the Groq Whisper API.
    /// </summary>
    /// <param name="wavBytes">Raw bytes of a valid WAV file.</param>
    /// <param name="fileName">
    ///   Virtual file name sent to the API. Must end in a supported extension
    ///   (.wav, .mp3, .flac, …). Defaults to <c>"audio.wav"</c>.
    /// </param>
    /// <returns>
    ///   The transcription text, or <see cref="string.Empty"/> when Groq
    ///   returned no speech.
    /// </returns>
    /// <remarks>
    ///   If the <see cref="GroqTranscriber"/> was configured with retry settings,
    ///   transient errors (network issues, server errors) will be retried up to the
    ///   specified number of times with exponential backoff before throwing.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    ///   Thrown when <paramref name="wavBytes"/> is null or empty.
    /// </exception>
    /// <exception cref="GroqTranscriberException">
    ///   Thrown on any HTTP or JSON error from the Groq API after retries exhausted.
    /// </exception>
    public async Task<string> TranscribeAsync(byte[] wavBytes, string fileName = "audio.wav", CancellationToken ct = default)
    {
        if (wavBytes == null || wavBytes.Length == 0)
            throw new ArgumentNullException(nameof(wavBytes), "WAV bytes must not be null or empty.");

        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();

        int attempt = 0;
        int maxAttempts = _retryCount + 1; // include initial attempt
        TimeSpan delay = TimeSpan.FromMilliseconds(_retryDelayMs);

        while (true)
        {
            attempt++;
            bool isLastAttempt = attempt >= maxAttempts;

            try
            {
                using var content = new MultipartFormDataContent();
                using var audioStream = new MemoryStream(wavBytes);

                var fileContent = new StreamContent(audioStream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

                content.Add(fileContent, "file", fileName);
                content.Add(new StringContent(_model), "model");
                content.Add(new StringContent("text"), "response_format");  // plain text response

                HttpResponseMessage response;
                try
                {
                    response = await _http.PostAsync(GroqApiUrl, content, ct).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    // Network error, maybe transient
                    if (isLastAttempt || !IsTransientError(ex))
                        throw new GroqTranscriberException("Network error while contacting Groq API.", ex);
                    
                    // Wait and retry
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                    delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * _retryBackoffFactor);
                    continue;
                }

                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    // Check if status code is transient
                    if (isLastAttempt || !IsTransientStatusCode(response.StatusCode))
                    {
                        throw new GroqTranscriberException(
                            $"Groq API returned HTTP {(int)response.StatusCode}: {body}");
                    }
                    
                    // Wait and retry
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                    delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * _retryBackoffFactor);
                    continue;
                }

                // When response_format=text the body IS the plain transcription string.
                // When the API wraps it in JSON ({"text":"…"}), we handle that too.
                return ExtractText(body);
            }
            catch (Exception ex) when (!isLastAttempt && IsTransientError(ex))
            {
                // This catch block is for any other transient exceptions we might have missed
                // Wait and retry
                await Task.Delay(delay, ct).ConfigureAwait(false);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * _retryBackoffFactor);
            }
        }
    }

    private static bool IsTransientError(Exception ex)
    {
        // Consider HttpRequestException as transient (network issues)
        return ex is HttpRequestException;
    }

    private static bool IsTransientStatusCode(System.Net.HttpStatusCode statusCode)
    {
        int code = (int)statusCode;
        // Retry on server errors (5xx) and too many requests (429)
        return code == 429 || (code >= 500 && code <= 599);
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Handles both a bare plain-text body and a JSON <c>{"text":"…"}</c> body.
    /// </summary>
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

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(GroqTranscriber));
    }

    // -----------------------------------------------------------------------
    // IDisposable
    // -----------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _http.Dispose();
        _disposed = true;
    }
}

// ---------------------------------------------------------------------------
// Exception type
// ---------------------------------------------------------------------------

/// <summary>
/// Thrown when the Groq transcription API returns an error.
/// </summary>
public sealed class GroqTranscriberException : Exception
{
    public GroqTranscriberException(string message) : base(message) { }
    public GroqTranscriberException(string message, Exception innerException) : base(message, innerException) { }
}
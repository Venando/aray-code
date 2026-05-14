using OpenClawPTT.TTS;

namespace OpenClawPTT.Services;

/// <summary>
/// Handles audio responses from the agent - detects [audio] markers,
/// synthesizes speech via TTS, and plays audio output.
/// </summary>
public sealed class AudioResponseHandler : IDisposable
{
    // ── TTS mode constants ─────────────────────────────────────────────
    private const string TtsModeOff = "off";
    private const string TtsModeSiso = "siso";
    private const string TtsModeAlwaysOn = "always-on";
    private const string TtsFallbackSkip = "skip";

    private readonly AppConfig _config;
    private readonly ITextToSpeech? _ttsProvider;
    private readonly IAudioPlayer _audioPlayer;
    private readonly ITtsSummarizer? _summarizer;
    private readonly IPttStateMachine? _pttStateMachine;
    // ── Retry constants ─────────────────────────────────────────────
    private const int SynthesisMaxRetries = 1;
    private static readonly TimeSpan SynthesisRetryDelay = TimeSpan.FromMilliseconds(500);

    private readonly IColorConsole _console;
    private readonly IBackgroundJobRunner _jobRunner;
    private readonly Action<bool>? _onSynthesisStatus;
    private readonly CancellationTokenSource _synthesisCts = new();
    private bool _disposed;

    public AudioResponseHandler(
        AppConfig config,
        IColorConsole console,
        IBackgroundJobRunner? jobRunner,
        IAudioPlayer audioPlayer,
        ITtsSummarizer? summarizer,
        IPttStateMachine? pttStateMachine,
        ITextToSpeech? ttsProvider,
        Action<bool>? onSynthesisStatus = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _jobRunner = jobRunner ?? new BackgroundJobRunner(msg => _console.Log("jobrunner", msg));
        _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));
        _summarizer = summarizer;
        _pttStateMachine = pttStateMachine;
        _ttsProvider = ttsProvider;
        _onSynthesisStatus = onSynthesisStatus;
    }

    public async Task PlayTtsAsync(string text, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AudioResponseHandler));

        if (!CanPlayTts(text, out var reason))
        {
            if (reason != null)
                _console.PrintWarning(reason);
            return;
        }

        var textToSpeak = await PrepareTextForTtsAsync(text, ct);
        if (string.IsNullOrWhiteSpace(textToSpeak))
            return;

        SynthesizeAndPlay(textToSpeak);
    }

    /// <summary>
    /// Validates conditions for TTS playback. Returns false with an optional warning message
    /// if playback should be skipped.
    /// </summary>
    private bool CanPlayTts(string text, out string? warning)
    {
        warning = null;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        // ── Config validation ────────────────────────────────────────────
        if (_config.TtsMaxChars <= 0)
        {
            warning = $"Invalid TtsMaxChars config ({_config.TtsMaxChars}) - must be greater than 0.";
            return false;
        }
        if (_config.TtsDirectMaxChars <= 0)
        {
            warning = $"Invalid TtsDirectMaxChars config ({_config.TtsDirectMaxChars}) - must be greater than 0.";
            return false;
        }

        // ── TTS mode checks ──────────────────────────────────────────────
        if (string.Equals(_config.TtsOutputMode, TtsModeOff, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(_config.TtsOutputMode, TtsModeSiso, StringComparison.OrdinalIgnoreCase))
        {
            if (_pttStateMachine == null || _pttStateMachine.LastInputWasVoice != true)
                return false;

            // Only speak if the response is from the same agent we sent the voice message to
            if (!string.IsNullOrEmpty(_pttStateMachine.LastTargetAgent) &&
                !string.Equals(_pttStateMachine.LastTargetAgent, AgentRegistry.ActiveAgentName, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // ── Suppress TTS during session history replay ───────────────────
        if (_pttStateMachine?.DuringReplay == true)
            return false;

        // ── TTS provider available? ──────────────────────────────────────
        if (_ttsProvider == null)
        {
            warning = "TTS not configured - set TtsProvider in settings to enable audio responses.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Prepares text for TTS: sanitizes, optionally summarizes via LLM, and truncates if needed.
    /// </summary>
    private async Task<string> PrepareTextForTtsAsync(string text, CancellationToken ct)
    {
        bool needsProcessing = TtsContentFilter.HasSpecialFormatting(text) ||
                               text.Length > _config.TtsDirectMaxChars;

        if (needsProcessing && _config.TtsUseDirectLlmSummary && _summarizer != null)
        {
            try
            {
                // Summarization timing and logging happens inside TtsSummarizer
                return await _summarizer.SummarizeForTtsAsync(text, _config, ct);
            }
            catch (Exception ex)
            {
                _console.PrintWarning($"TTS summarization failed: {ex.Message}. Falling back to truncation.");
                return TtsContentFilter.Truncate(
                    TtsContentFilter.SanitizeForTts(text), _config.TtsMaxChars);
            }
        }

        // No summarization available — sanitize and optionally truncate
        var sanitized = TtsContentFilter.SanitizeForTts(text);

        if (sanitized.Length > _config.TtsMaxChars)
        {
            if (string.Equals(_config.TtsTooLongFallback, TtsFallbackSkip, StringComparison.OrdinalIgnoreCase))
            {
                _console.PrintWarning(
                    $"Response ({sanitized.Length} chars) exceeds TtsMaxChars ({_config.TtsMaxChars}) — skipping TTS.");
                return string.Empty;
            }

            sanitized = TtsContentFilter.Truncate(sanitized, _config.TtsMaxChars);
        }

        return sanitized;
    }

    /// <summary>
    /// Synthesizes text to speech and plays the audio via a background job.
    /// Retries once on transient failure with a short delay.
    /// Reports synthesis status via <see cref="_onSynthesisStatus"/> callback.
    /// </summary>
    private void SynthesizeAndPlay(string textToSpeak)
    {
        var preview = textToSpeak.Length > 80 ? textToSpeak[..80] + "..." : textToSpeak;
        _jobRunner.RunAndForget(async () =>
        {
            byte[]? audioBytes = null;
            Exception? lastEx = null;

            for (int attempt = 0; attempt <= SynthesisMaxRetries; attempt++)
            {
                try
                {
                    audioBytes = await _ttsProvider!.SynthesizeAsync(
                        textToSpeak, _config.TtsVoice, null, _synthesisCts.Token);
                    lastEx = null;
                    break; // Success — exit retry loop
                }
                catch (OperationCanceledException)
                {
                    return; // Shutdown — don't report status
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    if (attempt < SynthesisMaxRetries)
                    {
                        _console.PrintWarning(
                            $"TTS synthesis attempt {attempt + 1} failed: {ex.Message}. Retrying...");
                        await Task.Delay(SynthesisRetryDelay, _synthesisCts.Token);
                    }
                }
            }

            if (lastEx != null)
            {
                _console.PrintWarning($"TTS synthesis failed after {SynthesisMaxRetries + 1} attempt(s): {lastEx.Message}");
                _onSynthesisStatus?.Invoke(false);
                return;
            }

            if (audioBytes != null && audioBytes.Length > 0)
            {
                _audioPlayer.Play(audioBytes);
                _onSynthesisStatus?.Invoke(true);
            }
            else
            {
                _console.PrintWarning("TTS synthesis returned null/empty audio.");
                _onSynthesisStatus?.Invoke(false);
            }
        }, $"tts-synthesis-{preview}");
    }

    /// <summary>
    /// Stop any currently playing audio.
    /// </summary>
    public void StopPlayback()
    {
        _audioPlayer.Stop();
    }

    /// <summary>
    /// Check if audio is currently playing.
    /// </summary>
    public bool IsPlaying => _audioPlayer.IsPlaying;

    public void Dispose()
    {
        if (!_disposed)
        {
            _synthesisCts.Cancel();
            _synthesisCts.Dispose();
            _audioPlayer.Dispose();
            (_jobRunner as IDisposable)?.Dispose();
            _disposed = true;
        }
    }
}
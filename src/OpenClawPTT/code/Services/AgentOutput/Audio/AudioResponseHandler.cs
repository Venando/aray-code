using OpenClawPTT.TTS;

namespace OpenClawPTT.Services;

/// <summary>
/// Handles audio responses from the agent - detects [audio] markers,
/// synthesizes speech via TTS, and plays audio output.
/// </summary>
public sealed class AudioResponseHandler : IDisposable
{
    private readonly AppConfig _config;
    private readonly ITextToSpeech? _ttsProvider;
    private readonly IAudioPlayer _audioPlayer;
    private readonly ITtsSummarizer? _summarizer;
    private readonly IPttStateMachine? _pttStateMachine;
    private readonly IColorConsole _console;
    private readonly IBackgroundJobRunner _jobRunner;
    private bool _disposed;

    public AudioResponseHandler(
        AppConfig config,
        IColorConsole console,
        IBackgroundJobRunner? jobRunner,
        IAudioPlayer audioPlayer,
        ITtsSummarizer? summarizer,
        IPttStateMachine? pttStateMachine,
        ITextToSpeech? ttsProvider)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _jobRunner = jobRunner ?? new BackgroundJobRunner(msg => _console.Log("jobrunner", msg));
        _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));
        _summarizer = summarizer;
        _pttStateMachine = pttStateMachine;
        _ttsProvider = ttsProvider;
    }

    /// <summary>
    /// Handle [audio] marker specifically - synthesize and play.
    /// </summary>
    public Task HandleAudioMarkerAsync(string text, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AudioResponseHandler));

        return PlayTtsAsync(text, ct);
    }

    private async Task PlayTtsAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        // Validate config values
        if (_config.TtsMaxChars <= 0)
        {
            _console.PrintWarning($"Invalid TtsMaxChars config ({_config.TtsMaxChars}) - must be greater than 0.");
            return;
        }
        if (_config.TtsDirectMaxChars <= 0)
        {
            _console.PrintWarning($"Invalid TtsDirectMaxChars config ({_config.TtsDirectMaxChars}) - must be greater than 0.");
            return;
        }

        // Check if TTS is enabled (case-insensitive)
        if (string.Equals(_config.TtsOutputMode, "off", StringComparison.OrdinalIgnoreCase))
            return;

        // Check SISO mode (case-insensitive)
        if (string.Equals(_config.TtsOutputMode, "siso", StringComparison.OrdinalIgnoreCase))
        {
            if (_pttStateMachine == null)
                return;
            if (_pttStateMachine.LastInputWasVoice != true)
                return;
            // SISO: only speak if the response is from the same agent we sent the voice message to
            if (!string.IsNullOrEmpty(_pttStateMachine.LastTargetAgent) &&
                !string.Equals(_pttStateMachine.LastTargetAgent, AgentRegistry.ActiveAgentName, StringComparison.OrdinalIgnoreCase))
                return;
        }

        // Suppress TTS during session history replay
        if (_pttStateMachine?.DuringReplay == true)
            return;

        if (_ttsProvider == null)
        {
            _console.PrintWarning("TTS not configured - set TtsProvider in settings to enable audio responses.");
            return;
        }

        // Determine if we need summarization
        var needsProcessing = TtsContentFilter.HasSpecialFormatting(text) ||
                              text.Length > _config.TtsDirectMaxChars;

        string textToSpeak;

        if (needsProcessing && _config.TtsUseDirectLlmSummary && _summarizer != null)
        {
            // Logging (model name + timing) is done inside TtsSummarizer
            try
            {
                textToSpeak = await _summarizer.SummarizeForTtsAsync(text, _config, ct);
            }
            catch (Exception ex)
            {
                _console.PrintWarning($"TTS summarization failed: {ex.Message}. Falling back to truncation.");
                textToSpeak = TtsContentFilter.SanitizeForTts(text);
                textToSpeak = TtsContentFilter.Truncate(textToSpeak, _config.TtsMaxChars);
            }
        }
        else if (needsProcessing)
        {
            // No Direct LLM available
            textToSpeak = TtsContentFilter.SanitizeForTts(text);

            if (textToSpeak.Length > _config.TtsMaxChars)
            {
                if (string.Equals(_config.TtsTooLongFallback, "skip", StringComparison.OrdinalIgnoreCase))
                {
                    _console.PrintWarning($"Response ({textToSpeak.Length} chars) exceeds TtsMaxChars ({_config.TtsMaxChars}) — skipping TTS.");
                    return;
                }
                else
                {
                    textToSpeak = TtsContentFilter.Truncate(textToSpeak, _config.TtsMaxChars);
                }
            }
        }
        else
        {
            // Short text, speak directly
            textToSpeak = TtsContentFilter.SanitizeForTts(text);
        }

        if (string.IsNullOrWhiteSpace(textToSpeak))
            return;

        // Synthesize and play via background job runner
        var truncated = textToSpeak.Length > 80 ? textToSpeak[..80] + "..." : textToSpeak;
        _jobRunner.RunAndForget(async () =>
        {
            var freshCt = CancellationToken.None;
            var audioBytes = await _ttsProvider.SynthesizeAsync(textToSpeak, _config.TtsVoice, null, freshCt);
            if (audioBytes != null && audioBytes.Length > 0)
            {
                _audioPlayer.Play(audioBytes);
            }
            else
            {
                _console.PrintWarning("TTS synthesis returned null/empty audio.");
            }
        }, $"tts-synthesis-{truncated}");
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
            _audioPlayer.Dispose();
            _disposed = true;
        }
    }
}
using System;
using System.Text.Json;
using OpenClawPTT.TTS;
using Spectre.Console;

namespace OpenClawPTT.Services;

/// <summary>
/// Subscribes to GatewayService domain events and forwards them to the appropriate
/// console output methods. This adapter is responsible for all UI concerns;
/// GatewayService itself only fires domain events.
/// </summary>
public sealed class AgentOutputAdapter : IDisposable
{
    private readonly IColorConsole _console;
    private readonly AppConfig _config;
    private readonly ToolDisplayHandler _toolDisplayHandler;
    private readonly IBackgroundJobRunner _jobRunner;
    private readonly AudioResponseHandler? _audioResponseHandler;
    private readonly ThinkingDisplayHandler _thinkingDisplay;
    private readonly ReplyStreamCoordinator _replyCoordinator;
    private bool _disposed;

    public AgentOutputAdapter(AppConfig config, IColorConsole console, ITtsSummarizer? summarizer = null, IPttStateMachine? pttStateMachine = null)
    {
        _config = config;
        _console = console ?? throw new ArgumentNullException(nameof(console));
        var shellHost = console.GetStreamShellHost();
        _toolDisplayHandler = new ToolDisplayHandler(_config.RightMarginIndent, shellHost);
        _thinkingDisplay = new ThinkingDisplayHandler(_config, shellHost);
        _replyCoordinator = new ReplyStreamCoordinator(_config, _console);
        _jobRunner = new BackgroundJobRunner(msg => _console.Log("jobrunner", msg));

        if (config.AudioResponseMode?.ToLowerInvariant() != "text-only")
        {
            var audioPlayer = new AudioPlayerService(console);
            ITextToSpeech? ttsProvider = null;
            try
            {
                var ttsService = new TtsService(config, console);
                ttsProvider = ttsService.Provider;
            }
            catch (Exception ex)
            {
                var hint = config.TtsProvider switch
                {
                    TtsProviderType.OpenAI => "Set TtsOpenAiApiKey or OpenAiApiKey in config.",
                    TtsProviderType.Coqui => "Verify PythonPath, CoquiModelName, and that Coqui TTS is installed (pip install TTS).",
                    TtsProviderType.Piper => "Verify PiperPath and that a voice model (.onnx file) is downloaded.",
                    TtsProviderType.Edge => "Set TtsSubscriptionKey (Azure API key) in config.",
                    TtsProviderType.ElevenLabs => "Set TtsApiKey and TtsVoiceId for ElevenLabs in config.",
                    _ => "Check provider configuration."
                };
                console.PrintWarning($"TTS provider initialization failed: {ex.Message} — {hint}");
            }
            _audioResponseHandler = new AudioResponseHandler(config, console, _jobRunner, audioPlayer, summarizer, pttStateMachine, ttsProvider);
        }
    }

    public AudioResponseHandler? AudioResponseHandler => _audioResponseHandler;

    public void AttachToService(IGatewayUIEvents service)
    {
        service.AgentReplyFull += OnAgentReplyFull;
        service.AgentThinking += OnAgentThinking;
        service.AgentToolCall += OnAgentToolCall;
        service.AgentReplyDeltaStart += OnAgentReplyDeltaStart;
        service.AgentReplyDelta += OnAgentReplyDelta;
        service.AgentReplyDeltaEnd += OnAgentReplyDeltaEnd;
        service.AgentReplyAudio += OnAgentReplyAudio;
    }

    public void DetachFromService(IGatewayUIEvents service)
    {
        service.AgentReplyFull -= OnAgentReplyFull;
        service.AgentThinking -= OnAgentThinking;
        service.AgentToolCall -= OnAgentToolCall;
        service.AgentReplyDeltaStart -= OnAgentReplyDeltaStart;
        service.AgentReplyDelta -= OnAgentReplyDelta;
        service.AgentReplyDeltaEnd -= OnAgentReplyDeltaEnd;
        service.AgentReplyAudio -= OnAgentReplyAudio;
    }

    // ─── event handlers ────────────────────────────────────────────

    public void OnAgentReplyFull(string body)
    {
        _replyCoordinator.OnFullReply(body);

        // Fire TTS for non-streaming (single-shot) responses
        if (_audioResponseHandler != null && !string.IsNullOrWhiteSpace(body))
        {
            _ = _audioResponseHandler.HandleAudioMarkerAsync(body);
        }
    }

    public void OnAgentThinking(string thinking)
    {
        if (_config.ThinkingDisplayMode != ThinkingMode.None)
        {
            _thinkingDisplay.DisplayThinking(thinking);
        }
    }

    public void OnAgentToolCall(string toolName, string arguments)
    {
        _toolDisplayHandler.Handle(toolName, arguments);
    }

    public void OnAgentReplyDeltaStart()
    {
        _replyCoordinator.OnDeltaStart();
    }

    public void OnAgentReplyDelta(string delta)
    {
        _replyCoordinator.OnDelta(delta);
    }

    public void OnAgentReplyDeltaEnd()
    {
        _replyCoordinator.OnDeltaEnd();

        // Fire TTS on accumulated text from streaming response
        if (_audioResponseHandler != null && !string.IsNullOrWhiteSpace(_replyCoordinator.AccumulatedText))
        {
            _ = _audioResponseHandler.HandleAudioMarkerAsync(_replyCoordinator.AccumulatedText);
        }
    }

    public void OnAgentReplyAudio(string audioText)
    {
        // [audio] markers are no longer the TTS trigger.
    }



    public void Dispose()
    {
        if (!_disposed)
        {
            _audioResponseHandler?.Dispose();
            _replyCoordinator.Dispose();
            _disposed = true;
        }
    }
}
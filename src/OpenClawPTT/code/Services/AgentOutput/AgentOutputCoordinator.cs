namespace OpenClawPTT.Services;

/// <summary>
/// Thin coordinator that routes IGatewayUIEvents to the appropriate
/// output services. Created by ServiceFactory with all dependencies injected.
/// Replaces AgentOutputAdapter.
/// </summary>
public sealed class AgentOutputCoordinator : IDisposable
{
    private readonly ReplyStreamCoordinator _replyCoordinator;
    private readonly ToolDisplayHandler _toolDisplayHandler;
    private readonly ThinkingDisplayHandler _thinkingDisplay;
    private AudioResponseHandler? _audioHandler;
    private bool _disposed;

    public AgentOutputCoordinator(
        ReplyStreamCoordinator replyCoordinator,
        ToolDisplayHandler toolDisplayHandler,
        ThinkingDisplayHandler thinkingDisplay,
        AudioResponseHandler? audioHandler)
    {
        _replyCoordinator = replyCoordinator ?? throw new ArgumentNullException(nameof(replyCoordinator));
        _toolDisplayHandler = toolDisplayHandler ?? throw new ArgumentNullException(nameof(toolDisplayHandler));
        _thinkingDisplay = thinkingDisplay ?? throw new ArgumentNullException(nameof(thinkingDisplay));
        _audioHandler = audioHandler;
    }

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

    /// <summary>Public entry points — GatewayService calls these directly.</summary>
    public void OnAgentReplyFull(string body)
    {
        _replyCoordinator.OnFullReply(body);
        if (_audioHandler != null && !string.IsNullOrWhiteSpace(body))
            _ = _audioHandler.HandleAudioMarkerAsync(body);
    }

    public void OnAgentThinking(string thinking)
        => _thinkingDisplay.DisplayThinking(thinking);

    public void OnAgentToolCall(string toolName, string arguments)
        => _toolDisplayHandler.Handle(toolName, arguments);

    public void OnAgentReplyDeltaStart() => _replyCoordinator.OnDeltaStart();

    public void OnAgentReplyDelta(string delta) => _replyCoordinator.OnDelta(delta);

    public void OnAgentReplyDeltaEnd()
    {
        _replyCoordinator.OnDeltaEnd();
        if (_audioHandler != null && !string.IsNullOrWhiteSpace(_replyCoordinator.AccumulatedText))
            _ = _audioHandler.HandleAudioMarkerAsync(_replyCoordinator.AccumulatedText);
    }

    /// <summary>
    /// Sets or replaces the audio handler after construction.
    /// Used when TTS initializes asynchronously (parallel with gateway connect).
    /// The previous handler (if any) is disposed before replacement.
    /// </summary>
    public void SetAudioHandler(AudioResponseHandler? handler)
    {
        if (_audioHandler == handler) return;
        _audioHandler?.Dispose();
        _audioHandler = handler;
    }

    public void OnAgentReplyAudio(string audioText)
    {
        // Audio markers handled by AudioResponseHandler — nothing to do here
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _audioHandler?.Dispose();
            _replyCoordinator.Dispose();
            _disposed = true;
        }
    }
}

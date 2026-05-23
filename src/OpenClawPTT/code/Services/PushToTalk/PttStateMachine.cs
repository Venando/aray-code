namespace OpenClawPTT.Services;

/// <summary>
/// Explicit PTT recording state machine.
/// State transitions:
///   Idle --[hotkey pressed]--> Recording --[released or toggle press]--> Processing --[complete]--> Idle
/// </summary>
public sealed class PttStateMachine : IPttStateMachine
{
    private PttState _state = PttState.Idle;

    /// <summary>Set true by OnHotkeyPressed while in Idle; consumed by ShouldStartRecording.</summary>
    private bool _startRecordingRequested;

    /// <summary>Set true by OnHotkeyReleased or OnHotkeyPressed (toggle) while in Recording; consumed by ShouldStopRecording.</summary>
    private bool _stopRecordingRequested;

    /// <summary>True when the last OnHotkeyPressed was a toggle (i.e. stop was requested while already Recording).</summary>
    private bool _toggleStopRequested;

    // Volatile for thread-safe cross-thread visibility (SISO mode TTS check)
    private volatile bool _lastInputWasVoice;

    public bool LastInputWasVoice
    {
        get => _lastInputWasVoice;
        set => _lastInputWasVoice = value;
    }


    public string? LastTargetAgent { get; set; }

    /// <summary>True while session history is being replayed — TTS should be suppressed.</summary>
    public bool DuringReplay { get; set; }

    public PttState CurrentState => _state;

    public bool ShouldStartRecording
    {
        get
        {
            if (_startRecordingRequested && _state == PttState.Recording)
            {
                _startRecordingRequested = false;
                return true;
            }
            return false;
        }
    }

    public bool ShouldStopRecording
    {
        get
        {
            if (_stopRecordingRequested && _state == PttState.Processing)
            {
                _stopRecordingRequested = false;
                return true;
            }
            return false;
        }
    }

    public bool ShouldToggleRecording => _toggleStopRequested && _state == PttState.Recording;

    public void OnHotkeyPressed()
    {
        switch (_state)
        {
            case PttState.Idle:
                // Reset voice-input tracking — a new recording supersedes any previous one
                _lastInputWasVoice = false;
                _state = PttState.Recording;
                _startRecordingRequested = true;
                _toggleStopRequested = false;
                break;

            case PttState.Recording:
                // Toggle mode: stop recording on second press
                _toggleStopRequested = true;
                _stopRecordingRequested = true;
                _state = PttState.Processing;
                break;

            case PttState.Processing:
                // Ignore while processing
                break;
        }
    }

    public void OnHotkeyReleased()
    {
        if (_state == PttState.Recording)
        {
            _stopRecordingRequested = true;
            _state = PttState.Processing;
        }
    }

    public void OnRecordingStarted()
    {
        // No state change — recording started confirms the transition to Recording
    }

    public void OnProcessingCompleted()
    {
        _state = PttState.Idle;
        _toggleStopRequested = false;
    }

    public void Reset()
    {
        _state = PttState.Idle;
        _startRecordingRequested = false;
        _stopRecordingRequested = false;
        _toggleStopRequested = false;
        _lastInputWasVoice = false;
    }
}

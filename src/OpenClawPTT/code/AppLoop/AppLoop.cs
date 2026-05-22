using System;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;

namespace OpenClawPTT.Services;

/// <summary>
/// The main PTT event loop. Coordinates hotkey state machine, audio recording,
/// transcription, and console input polling.
/// Confirmation dialog extracted to its own method for SRP.
/// </summary>
public sealed class AppLoop : IAppLoop
{
    private readonly IPttStateMachine _pttStateMachine;
    private readonly IAudioService _audioService;
    private readonly ITextMessageSender _textSender;
    private readonly IInputHandler _inputHandler;
    private readonly IPttController _pttController;
    private readonly IColorConsole _console;
    private readonly bool _requireConfirmBeforeSend;
    private bool _disposed;


    public AppLoop(
        IPttStateMachine stateMachine,
        IAudioService audioService,
        ITextMessageSender textSender,
        IInputHandler inputHandler,
        IPttController pttController,
        IColorConsole console,
        bool requireConfirmBeforeSend = false)
    {
        _pttStateMachine = stateMachine;
        _audioService = audioService;
        _textSender = textSender;
        _inputHandler = inputHandler;
        _pttController = pttController;
        _console = console;
        _requireConfirmBeforeSend = requireConfirmBeforeSend;
    }

    public AppLoopExitCode ExitCode { get; private set; } = AppLoopExitCode.Ok;

    public async Task<AppLoopExitCode> RunAsync(CancellationToken ct)
    {
        if (_disposed) return AppLoopExitCode.Ok;
        _pttStateMachine.Reset();

        while (!ct.IsCancellationRequested)
        {
            await PollHotkeyState();
            await HandleRecordingState(ct);

            // Console input is now handled by StreamShell via StreamShellInputHandler

            // Yield: prevent spin-loop. Without this the while-loop burns 100% CPU
            // polling hotkey flags that are almost always false.
            try { await Task.Delay(50, ct); }
            catch (OperationCanceledException) { break; }
        }

        return AppLoopExitCode.Ok;
    }

    /// <summary>Polls the hardware for hotkey state transitions.</summary>
    private async Task PollHotkeyState()
    {
        if (_pttController.PollHotkeyPressed())
            _pttStateMachine.OnHotkeyPressed();

        if (_pttController.PollHotkeyRelease())
            _pttStateMachine.OnHotkeyReleased();

        if (_pttController.PollCancelRecording())
        {
            _pttStateMachine.Reset();
            _audioService.StopDiscard();
        }

        await Task.CompletedTask;
    }

    /// <summary>Handles state-driven recording actions (start, stop, transcribe).</summary>
    private async Task HandleRecordingState(CancellationToken ct)
    {
        if (_pttStateMachine.ShouldStartRecording && !_audioService.IsRecording)
        {
            _audioService.StartRecording();
            _pttStateMachine.OnRecordingStarted();
        }

        if (_pttStateMachine.ShouldStopRecording)
        {
            await HandleRecordingComplete(ct);
        }
    }

    /// <summary>Handles the recording-complete path: transcribe + send (with optional confirmation).</summary>
    private async Task HandleRecordingComplete(CancellationToken ct)
    {
        var transcribed = await _audioService.StopAndTranscribeAsync(ct);
        if (transcribed != null)
        {
            if (_requireConfirmBeforeSend)
            {
                bool sent = await WaitForSendConfirmationAsync(ct);
                if (sent)
                {
                    _audioService.DismissRecordingPanel();
                    await SendTranscribedMessage(transcribed, ct);
                }
                else
                {
                    _audioService.ShowDiscarded();
                }
            }
            else
            {
                _audioService.DismissRecordingPanel();
                await SendTranscribedMessage(transcribed, ct);
            }
        }
        _pttStateMachine.OnProcessingCompleted();
    }

    /// <summary>
    /// Shows confirmation prompt and waits for user action.
    /// Returns true if the user confirmed sending, false if discarded.
    /// The confirmation UI is shown in the bottom panel by AudioService.
    /// </summary>
    private async Task<bool> WaitForSendConfirmationAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(50, ct);
            if (_pttController.PollHotkeyPressed())
                return true;

            if (_pttController.PollCancelRecording())
                return false;
        }

        return false;
    }

    private async Task SendTranscribedMessage(string transcribed, CancellationToken ct)
    {
        try
        {
            _pttStateMachine.LastInputWasVoice = true;
            _pttStateMachine.LastTargetAgent = AgentRegistry.ActiveAgentName;
            await _textSender.SendAsync(transcribed, ct);
        }
        catch (Exception ex)
        {
            _console.LogError("ptt", $"Failed to send: {ex.GetType().Name}: {ex.Message}");
            // Preserve transcribed text so the user can re-send manually
            // (escape brackets to prevent Spectre markup injection from transcribed speech)
            var safe = Markup.Escape(transcribed);
            _console.PrintMarkup($"[yellow][dim]  Transcribed: [/][/][yellow]{safe}[/]");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _pttController.Dispose();
            _audioService.Dispose();
            _disposed = true;
        }
    }
}

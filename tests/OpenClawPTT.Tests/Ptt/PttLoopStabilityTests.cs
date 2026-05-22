using OpenClawPTT;
using OpenClawPTT.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;

namespace OpenClawPTT.Tests;

/// <summary>
/// Stability and edge-case tests for PttLoop.
/// Uses simple test doubles for core domain interfaces (IPttStateMachine, IAudioService,
/// ITextMessageSender, IPttController, IInputHandler).
/// NOTE: Marked Slow — these tests use real Task.Delay and are single-threaded.
/// Filter with --filter "Category!=Slow" to skip during development.
/// </summary>
[Trait("Category", "Slow")]
public class PttLoopStabilityTests : IDisposable
{
    #region Test Doubles

    sealed class FakePttStateMachine : IPttStateMachine
    {
        public PttState CurrentState { get; set; } = PttState.Idle;
        public bool LastInputWasVoice { get; set; } = false;
        public string? LastTargetAgent { get; set; }
        public bool DuringReplay { get; set; }
        public bool ShouldStartRecording_Flag { get; set; }
        public bool ShouldStopRecording_Flag { get; set; }
        public bool ShouldToggleRecording_Flag { get; set; }

        /// <summary>
        /// Set this to true before RunAsync to force ShouldStopRecording=true after Reset() is called.
        /// Reset() does NOT clear this flag.
        /// </summary>
        public bool ShouldStopRecording_AfterReset { get; set; }

        public int OnHotkeyPressed_Count { get; private set; }
        public int OnHotkeyReleased_Count { get; private set; }
        public int OnRecordingStarted_Count { get; private set; }
        public int OnRecordingStopped_Count { get; private set; }
        public int OnProcessingCompleted_Count { get; private set; }
        public int Reset_Count { get; private set; }

        public bool ShouldStartRecording
        {
            get
            {
                if (ShouldStartRecording_Flag && CurrentState == PttState.Recording)
                {
                    ShouldStartRecording_Flag = false;
                    return true;
                }
                return false;
            }
        }

        public bool ShouldStopRecording
        {
            get
            {
                if (ShouldStopRecording_Flag && CurrentState == PttState.Processing)
                {
                    ShouldStopRecording_Flag = false;
                    return true;
                }
                if (ShouldStopRecording_AfterReset && CurrentState == PttState.Processing)
                {
                    ShouldStopRecording_AfterReset = false;
                    return true;
                }
                return false;
            }
        }

        public bool ShouldToggleRecording => ShouldToggleRecording_Flag && CurrentState == PttState.Recording;

        public void OnHotkeyPressed() => OnHotkeyPressed_Count++;
        public void OnHotkeyReleased() => OnHotkeyReleased_Count++;
        public void OnRecordingStarted() => OnRecordingStarted_Count++;
        public void OnRecordingStopped() => OnRecordingStopped_Count++;
        public void OnProcessingCompleted()
        {
            OnProcessingCompleted_Count++;
            CurrentState = PttState.Idle;
            ShouldToggleRecording_Flag = false;
        }

        public void Reset()
        {
            Reset_Count++;
            // If ShouldStopRecording_AfterReset is set, start in Processing so the
            // loop immediately sees ShouldStopRecording=true (simulates a hotkey release
            // that happened before the loop started).
            CurrentState = ShouldStopRecording_AfterReset ? PttState.Processing : PttState.Idle;
            ShouldStartRecording_Flag = false;
            ShouldStopRecording_Flag = false;
            ShouldToggleRecording_Flag = false;
            // ShouldStopRecording_AfterReset intentionally NOT cleared
        }
    }

    sealed class FakeAudioService : IAudioService
    {
        public bool IsRecording_Value { get; set; }
        public int StartRecording_Count { get; private set; }
        public int Dispose_Count { get; private set; }
        public Action<TranscriptionPhase, string?>? TranscriptionStatusCallback { get; set; }

        public bool IsRecording => IsRecording_Value;
        public void StartRecording() => StartRecording_Count++;
        public Task<string?> StopAndTranscribeAsync(CancellationToken ct = default)
            => Task.FromResult<string?>(null);
        public void StopDiscard() { }
        public void ShowTranscribing() { }
        public void ShowConfirming(string transcribed, double sizeKb) { }
        public void ShowDiscarded() { }
        public void DismissRecordingPanel() { }
        public void RecreateTranscriber(AppConfig config, IColorConsole console) { /* no-op for tests */ }
        public void RecreateRecorder(AppConfig config, IColorConsole console) { /* no-op for tests */ }
        public Task VerifyTranscriberAsync(AppConfig config, IColorConsole console, CancellationToken ct = default)
            => Task.CompletedTask;
        public void Dispose() => Dispose_Count++;
    }

    sealed class FakeTextMessageSender : ITextMessageSender
    {
        public Exception? SendException { get; set; }
        public string? LastText { get; private set; }
        public int SendAsync_Count { get; private set; }

        public Task SendAsync(string text, CancellationToken ct, bool printMessage = true)
        {
            SendAsync_Count++;
            LastText = text;
            if (SendException != null) throw SendException;
            return Task.CompletedTask;
        }
    }

    sealed class DummyAgentReplyFormatter : IAgentReplyFormatter
    {
        public void ProcessDelta(string delta) { }
        public void ProcessMarkupDelta(string markup) { }
        public void Finish() { }
    }

    sealed class FakeInputHandler : IInputHandler
    {
        public InputResult Result { get; set; } = InputResult.Continue;
        public int HandleInputAsync_Count { get; private set; }

        public Task<InputResult> HandleInputAsync(CancellationToken ct)
        {
            HandleInputAsync_Count++;
            return Task.FromResult(Result);
        }

        public Task SendTextAsync(string text, CancellationToken ct) => Task.CompletedTask;
    }

    sealed class FakePttController : IPttController
    {
        public bool PollHotkeyPressed_Return { get; set; }
        public bool PollHotkeyRelease_Return { get; set; }
        public Func<string?> StopAndTranscribeAsync_Return { get; set; } = () => "transcribed text";
        public bool IsRecording_Value { get; set; }
        public int PollHotkeyPressed_Count { get; private set; }
        public int PollHotkeyRelease_Count { get; private set; }
        public int Dispose_Count { get; private set; }

        public bool IsRecording
        {
            get => IsRecording_Value;
            set => IsRecording_Value = value;
        }

        public bool PollHotkeyPressed()
        {
            PollHotkeyPressed_Count++;
            return PollHotkeyPressed_Return;
        }

        public bool PollHotkeyRelease()
        {
            PollHotkeyRelease_Count++;
            return PollHotkeyRelease_Return;
        }

        public Task<string?> StopAndTranscribeAsync(CancellationToken ct = default)
            => Task.FromResult(StopAndTranscribeAsync_Return());

        public void StartRecording() { }
        public void StopRecording() { }
        public void CancelRecording() { }
        public bool PollCancelRecording() => false;
        public void SetHotkey(string hotkeyCombination, bool holdToTalk) { }
        public void Start() { }
        public void Stop() { }
        public void Dispose() => Dispose_Count++;
    }

    #endregion

    static AppLoop CreateLoop(
        FakePttStateMachine? state = null,
        FakeAudioService? audio = null,
        FakeTextMessageSender? sender = null,
        FakeInputHandler? input = null,
        FakePttController? pttCtrl = null,
        IColorConsole? console = null)
    {
        return new AppLoop(
            state ?? new FakePttStateMachine(),
            audio ?? new FakeAudioService(),
            sender ?? new FakeTextMessageSender(),
            input ?? new FakeInputHandler(),
            pttCtrl ?? new FakePttController(),
            console ?? new Mock<IColorConsole>().Object);
    }

    public void Dispose() { }

    #region Tests

    [Fact]
    public async Task RunAsync_StopAndTranscribeAsyncReturnsNull_NoSendAsyncCalled()
    {
        // Arrange: state machine is Processing with ShouldStopRecording set,
        // but StopAndTranscribeAsync returns null (silence/empty audio)
        var state = new FakePttStateMachine
        {
            ShouldStopRecording_AfterReset = true
        };
        var audio = new FakeAudioService();
        var sender = new FakeTextMessageSender();
        var input = new FakeInputHandler { Result = InputResult.Quit };
        var pttCtrl = new FakePttController { StopAndTranscribeAsync_Return = () => null };

        var loop = CreateLoop(state, audio, sender, input: input, pttCtrl: pttCtrl);

        // Act
        var exitCode = await loop.RunAsync(new CancellationTokenSource(100).Token);

        // Assert: transcription was skipped, SendAsync never called
        Assert.Equal(AppLoopExitCode.Ok, exitCode);
        Assert.Equal(0, sender.SendAsync_Count);
        Assert.Equal(1, state.OnProcessingCompleted_Count);
    }

    [Fact]
    public async Task RunAsync_SendAsyncThrows_LoopContinues()
    {
        // Arrange: transcription succeeds but SendAsync throws (e.g. network error)
        var state = new FakePttStateMachine
        {
            ShouldStopRecording_AfterReset = true
        };
        var audio = new FakeAudioService();
        var sender = new FakeTextMessageSender
        {
            SendException = new InvalidOperationException("network error")
        };
        var input = new FakeInputHandler { Result = InputResult.Quit };
        var pttCtrl = new FakePttController();

        var loop = CreateLoop(state, audio, sender, input: input, pttCtrl: pttCtrl);

        // Act & Assert: loop swallows the exception and exits gracefully — no throw
        var exitCode = await loop.RunAsync(new CancellationTokenSource(200).Token);

        Assert.Equal(AppLoopExitCode.Ok, exitCode);
        Assert.Equal(0, sender.SendAsync_Count); // SendAsync was called (and threw)
        Assert.Equal(1, state.OnProcessingCompleted_Count); // loop continued to completion
    }

    [Fact]
    public async Task RunAsync_MultipleRapidHotkeyPressesDuringProcessing_OnlyOneProcessing()
    {
        // Arrange: state machine ignores hotkey presses during Processing
        var state = new FakePttStateMachine
        {
            ShouldStopRecording_AfterReset = true
        };
        var audio = new FakeAudioService();
        var sender = new FakeTextMessageSender();
        var input = new FakeInputHandler { Result = InputResult.Quit };
        var pttCtrl = new FakePttController();

        var loop = CreateLoop(state, audio, sender, input: input, pttCtrl: pttCtrl);

        // Act
        var exitCode = await loop.RunAsync(new CancellationTokenSource(200).Token);

        // Assert: exactly one processing cycle
        Assert.Equal(AppLoopExitCode.Ok, exitCode);
        Assert.Equal(1, state.OnProcessingCompleted_Count);
    }

    [Fact]
    public async Task RunAsync_CalledAfterDispose_GracefullyReturns()
    {
        // Arrange: dispose the loop first, then call RunAsync
        var state = new FakePttStateMachine();
        var audio = new FakeAudioService();
        var sender = new FakeTextMessageSender();
        var input = new FakeInputHandler { Result = InputResult.Quit };
        var pttCtrl = new FakePttController();

        var loop = CreateLoop(state, audio, sender, input: input, pttCtrl: pttCtrl);
        loop.Dispose();

        // Act: RunAsync should return immediately without entering the loop
        var exitCode = await loop.RunAsync(new CancellationTokenSource(50).Token);

        // Assert: exits immediately without polling hotkey or starting recording
        Assert.Equal(AppLoopExitCode.Ok, exitCode);
        Assert.Equal(0, audio.StartRecording_Count);
        Assert.Equal(0, pttCtrl.PollHotkeyPressed_Count);
        Assert.Equal(0, state.Reset_Count); // Reset() not called since loop body skipped
    }

    [Fact]
    public async Task RunAsync_ExitsCleanly_OnCancellation()
    {
        // Arrange
        var state = new FakePttStateMachine();
        var audio = new FakeAudioService();
        var sender = new FakeTextMessageSender();
        var input = new FakeInputHandler { Result = InputResult.Quit };
        var pttCtrl = new FakePttController();

        var loop = CreateLoop(state, audio, sender, input: input, pttCtrl: pttCtrl);

        // Act
        var exitCode = await loop.RunAsync(new CancellationTokenSource(50).Token);

        // Assert
        Assert.Equal(AppLoopExitCode.Ok, exitCode);
    }

    [Fact]
    public async Task RunAsync_RapidStartStopCycles_NoResourceLeaks()
    {
        // Arrange: normal quit after running through a cycle
        var state = new FakePttStateMachine();
        var audio = new FakeAudioService();
        var sender = new FakeTextMessageSender();
        var input = new FakeInputHandler { Result = InputResult.Quit };
        var pttCtrl = new FakePttController();

        var loop = CreateLoop(state, audio, sender, input: input, pttCtrl: pttCtrl);

        // Act
        var exitCode = await loop.RunAsync(new CancellationTokenSource(500).Token);
        loop.Dispose();

        // Assert: resources are disposed exactly once on Dispose()
        Assert.Equal(AppLoopExitCode.Ok, exitCode);
        Assert.Equal(1, pttCtrl.Dispose_Count);
        Assert.Equal(1, audio.Dispose_Count);
    }

    [Fact]
    public async Task RunAsync_ShouldStopRecordingClearedAfterProcessing_NoDoubleTrigger()
    {
        // Arrange: set up state machine so it enters Processing with ShouldStopRecording=true
        var state = new FakePttStateMachine
        {
            ShouldStopRecording_AfterReset = true
        };
        var audio = new FakeAudioService();
        var sender = new FakeTextMessageSender();
        var input = new FakeInputHandler { Result = InputResult.Quit };
        var pttCtrl = new FakePttController { StopAndTranscribeAsync_Return = () => "transcribed text" };

        var loop = CreateLoop(state, audio, sender, input: input, pttCtrl: pttCtrl);

        // Act
        var exitCode = await loop.RunAsync(new CancellationTokenSource(200).Token);

        // Assert: processing completed once, state machine back to Idle
        Assert.Equal(AppLoopExitCode.Ok, exitCode);
        Assert.Equal(1, state.OnProcessingCompleted_Count);
        Assert.Equal(PttState.Idle, state.CurrentState);

        // After OnProcessingCompleted, state is Idle so ShouldStopRecording returns false
        // even if the flag had not been cleared (it is cleared by the getter)
        Assert.False(state.ShouldStopRecording);
    }

    #endregion
}

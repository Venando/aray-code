using ArayCode.Services;
using Xunit;

namespace ArayCode.Tests;

public class PttStateMachineTests
{
    [Fact]
    public void InitialState_IsIdle()
    {
        var sm = new PttStateMachine();
        Assert.Equal(PttState.Idle, sm.CurrentState);
    }

    [Fact]
    public void Idle_OnHotkeyPressed_TransitionsToRecording_AndShouldStartRecording()
    {
        var sm = new PttStateMachine();

        sm.OnHotkeyPressed();

        Assert.Equal(PttState.Recording, sm.CurrentState);
        Assert.True(sm.ShouldStartRecording);
    }

    [Fact]
    public void Idle_OnHotkeyPressed_ClearsShouldStartRecordingAfterRead()
    {
        var sm = new PttStateMachine();
        sm.OnHotkeyPressed();
        _ = sm.ShouldStartRecording; // consume

        Assert.False(sm.ShouldStartRecording);
    }

    [Fact]
    public void Recording_OnHotkeyReleased_TransitionsToProcessing_AndShouldStopRecording()
    {
        var sm = new PttStateMachine();
        sm.OnHotkeyPressed(); // Idle -> Recording

        sm.OnHotkeyReleased(); // Recording -> Processing

        Assert.Equal(PttState.Processing, sm.CurrentState);
        Assert.True(sm.ShouldStopRecording);
    }

    [Fact]
    public void Recording_OnHotkeyPressed_TogglesToProcessing_AndShouldStopRecording()
    {
        var sm = new PttStateMachine();
        sm.OnHotkeyPressed(); // Idle -> Recording

        sm.OnHotkeyPressed(); // Recording -> Processing (toggle)

        Assert.Equal(PttState.Processing, sm.CurrentState);
        Assert.True(sm.ShouldStopRecording);
        // ShouldToggleRecording is only true while state is still Recording
        Assert.False(sm.ShouldToggleRecording);
    }

    [Fact]
    public void Recording_OnHotkeyPressed_ConsumesStopFlag_ThenStopIsFalse()
    {
        var sm = new PttStateMachine();
        sm.OnHotkeyPressed(); // Idle -> Recording
        sm.OnHotkeyPressed(); // Recording -> Processing
        _ = sm.ShouldStopRecording; // consume

        // After consuming, ShouldStopRecording is false
        Assert.False(sm.ShouldStopRecording);
        Assert.False(sm.ShouldToggleRecording);
    }

    [Fact]
    public void Processing_OnProcessingCompleted_TransitionsToIdle()
    {
        var sm = new PttStateMachine();
        sm.OnHotkeyPressed();
        sm.OnHotkeyReleased();

        sm.OnProcessingCompleted();

        Assert.Equal(PttState.Idle, sm.CurrentState);
    }

    [Fact]
    public void Processing_OnHotkeyPressed_DoesNothing()
    {
        var sm = new PttStateMachine();
        sm.OnHotkeyPressed();
        sm.OnHotkeyReleased();

        sm.OnHotkeyPressed(); // should be ignored in Processing

        Assert.Equal(PttState.Processing, sm.CurrentState);
    }

    [Theory]
    [InlineData(PttState.Idle)]
    [InlineData(PttState.Recording)]
    [InlineData(PttState.Processing)]
    public void Reset_FromAnyState_ReturnsToIdle(PttState initialState)
    {
        var sm = new PttStateMachine();

        // Force state machine into the given state
        switch (initialState)
        {
            case PttState.Idle:
                // Already idle
                break;
            case PttState.Recording:
                sm.OnHotkeyPressed();
                break;
            case PttState.Processing:
                sm.OnHotkeyPressed();
                sm.OnHotkeyReleased();
                break;
        }

        sm.Reset();

        Assert.Equal(PttState.Idle, sm.CurrentState);
        Assert.False(sm.ShouldStartRecording);
        Assert.False(sm.ShouldStopRecording);
    }
}

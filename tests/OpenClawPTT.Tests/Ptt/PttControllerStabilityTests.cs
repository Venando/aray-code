using Moq;
using OpenClawPTT;
using OpenClawPTT.Services;
using System;
using System.Collections.Generic;

namespace OpenClawPTT.Tests;

/// <summary>
/// Stability and edge-case tests for PttController.
/// These complement PttControllerTests which cover the happy path.
/// </summary>
public class PttControllerStabilityTests : IDisposable
{
    /// <summary>
    /// Test double for IGlobalHotkeyHook that tracks calls and lets us fire events.
    /// </summary>
    private sealed class FakeHotkeyHook : IGlobalHotkeyHook
    {
        private Action? _hotkeyPressed;
        private Action? _hotkeyReleased;

        event Action? IGlobalHotkeyHook.HotkeyPressed
        {
            add { _hotkeyPressed += value; }
            remove { _hotkeyPressed -= value; }
        }
        event Action? IGlobalHotkeyHook.HotkeyReleased
        {
            add { _hotkeyReleased += value; }
            remove { _hotkeyReleased -= value; }
        }
        event Action<int>? IGlobalHotkeyHook.HotkeyIndexPressed { add { } remove { } }
        event Action<int>? IGlobalHotkeyHook.HotkeyIndexReleased { add { } remove { } }
        event Action? IGlobalHotkeyHook.EscapePressed { add { } remove { } }

        public void SetHotkey(Hotkey hotkey) { }
        public void SetHotkeys(System.Collections.Generic.IEnumerable<Hotkey> hotkeys) { }
        public bool BlockEscape { get; set; }
        public void Start() { }
        public void Dispose() { }
    }

    /// <summary>
    /// Test double for IHotkeyHookFactory.
    /// </summary>
    private sealed class FakeHotkeyHookFactory : IHotkeyHookFactory
    {
        public IGlobalHotkeyHook Create(Hotkey mapping, IColorConsole console)
            => new FakeHotkeyHook();
    }

    public void Dispose() { }

    // ── PollHotkeyPressed tests (using external trigger) ─────────────────────

    [Fact]
    void PollHotkeyPressed_InitiallyFalse()
    {
        var controller = new PttController();
        Assert.False(controller.PollHotkeyPressed());
    }

    [Fact]
    void PollHotkeyPressed_AfterStartRecording_ReturnsTrueOnce()
    {
        var controller = new PttController();
        controller.StartRecording();

        Assert.True(controller.PollHotkeyPressed());
        Assert.False(controller.PollHotkeyPressed()); // consumed
    }

    [Fact]
    void PollHotkeyPressed_MultipleStartRecording_EachConsumedSeparately()
    {
        var controller = new PttController();
        controller.StartRecording(); // first press

        Assert.True(controller.PollHotkeyPressed()); // first press consumed
        Assert.False(controller.PollHotkeyPressed()); // already consumed

        controller.StartRecording(); // second press
        Assert.True(controller.PollHotkeyPressed()); // second press consumed
        Assert.False(controller.PollHotkeyPressed()); // consumed again
    }

    // ── PollHotkeyRelease tests (using external trigger) ─────────────────────

    [Fact]
    void PollHotkeyRelease_InitiallyFalse()
    {
        var controller = new PttController();
        Assert.False(controller.PollHotkeyRelease());
    }

    [Fact]
    void PollHotkeyRelease_AfterStopRecording_ReturnsTrueOnce()
    {
        var controller = new PttController();
        controller.StartRecording();
        controller.StopRecording();

        Assert.True(controller.PollHotkeyRelease());
        Assert.False(controller.PollHotkeyRelease()); // consumed
    }

    [Fact]
    void PollHotkeyRelease_MultipleStops_EachConsumedSeparately()
    {
        var controller = new PttController();
        controller.StartRecording();
        controller.StopRecording(); // first stop

        Assert.True(controller.PollHotkeyRelease()); // first stop consumed
        Assert.False(controller.PollHotkeyRelease()); // already consumed

        controller.StartRecording();
        controller.StopRecording(); // second stop
        Assert.True(controller.PollHotkeyRelease()); // second stop consumed
        Assert.False(controller.PollHotkeyRelease()); // consumed again
    }

    // ── CancelRecording tests ──────────────────────────────────────────────

    [Fact]
    void CancelRecording_ClearsBothFlags()
    {
        var controller = new PttController();
        controller.StartRecording();
        controller.CancelRecording();

        Assert.False(controller.PollHotkeyPressed());
        Assert.False(controller.PollHotkeyRelease());
        Assert.True(controller.PollCancelRecording());
    }

    // ── Dispose tests ──────────────────────────────────────────────────────

    [Fact]
    void Dispose_CalledTwice_DoesNotThrow()
    {
        var mockConsole = new Mock<IColorConsole>();
        var controller = new PttController(new FakeHotkeyHookFactory(), mockConsole.Object);
        controller.SetHotkey("Ctrl+K", false);

        controller.Dispose();

        var ex = Record.Exception(() => controller.Dispose());
        Assert.Null(ex);
    }
}

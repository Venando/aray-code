using System.Collections.Generic;

namespace ArayCode;

/// <summary>
/// No-op implementation of <see cref="IGlobalHotkeyHook"/> used when
/// no platform-specific hotkey mechanism is available.
/// All events are never fired, all methods are no-ops.
/// </summary>
#pragma warning disable CS0067 // events are intentionally never fired
internal sealed class NoopHotkeyHook : IGlobalHotkeyHook
{
    public event Action? HotkeyPressed;
    public event Action? HotkeyReleased;
    public event Action<int>? HotkeyIndexPressed;
    public event Action<int>? HotkeyIndexReleased;
    public event Action? EscapePressed;
    public bool BlockEscape { get; set; }

    public void SetHotkey(Hotkey hotkey) { }
    public void SetHotkeys(IEnumerable<Hotkey> hotkeys) { }
    public void Start() { }
    public void Dispose() { }
}

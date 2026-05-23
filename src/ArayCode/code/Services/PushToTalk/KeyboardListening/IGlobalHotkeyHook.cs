using System.Collections.Generic;

namespace ArayCode;

public interface IGlobalHotkeyHook : IDisposable
{
    /// <summary>Fired when ANY configured hotkey is pressed (key down).</summary>
    event Action? HotkeyPressed;
    /// <summary>Fired when ANY configured hotkey is released (key up).</summary>
    event Action? HotkeyReleased;

    /// <summary>Fired with the index of the matched hotkey when pressed.</summary>
    event Action<int>? HotkeyIndexPressed;
    /// <summary>Fired with the index of the matched hotkey when released.</summary>
    event Action<int>? HotkeyIndexReleased;

    /// <summary>Single hotkey — backwards compatible, calls SetHotkeys(new[] { hotkey }).</summary>
    void SetHotkey(Hotkey hotkey);

    /// <summary>Replace all hotkeys with the given list.</summary>
    void SetHotkeys(IEnumerable<Hotkey> hotkeys);

    /// <summary>Fired when the Escape key is pressed (for cancelling recording).</summary>
    event Action? EscapePressed;

    /// <summary>When true, the hook blocks Escape from reaching the console and fires EscapePressed instead.</summary>
    bool BlockEscape { get; set; }

    void Start();
}
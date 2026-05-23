using ArayCode.Services;

namespace ArayCode;

/// <summary>
/// Factory for creating IGlobalHotkeyHook instances. Allows PttController
/// to be tested without registering real global hotkeys.
/// </summary>
public interface IHotkeyHookFactory
{
    IGlobalHotkeyHook Create(Hotkey mapping, IColorConsole console);
}

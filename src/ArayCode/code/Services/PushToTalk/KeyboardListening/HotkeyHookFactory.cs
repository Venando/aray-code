using ArayCode.Services;

namespace ArayCode;

/// <summary>
/// Default implementation of IHotkeyHookFactory that delegates to GlobalHotkeyHookFactory.
/// </summary>
internal sealed class HotkeyHookFactory : IHotkeyHookFactory
{
    public IGlobalHotkeyHook Create(Hotkey mapping, IColorConsole console)
    {
        var hook = GlobalHotkeyHookFactory.Create(console);
        hook.SetHotkey(mapping);
        return hook;
    }
}

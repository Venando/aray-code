using ArayCode.Services;

namespace ArayCode;

/// <summary>
/// Default implementation of IHotkeyHookFactory that delegates to GlobalHotkeyHookFactory.
/// </summary>
internal sealed class HotkeyHookFactory : IHotkeyHookFactory
{
    private readonly AppConfig? _config;
    private readonly IStreamShellHost? _shellHost;

    public HotkeyHookFactory(AppConfig? config = null, IStreamShellHost? shellHost = null)
    {
        _config = config;
        _shellHost = shellHost;
    }

    public IGlobalHotkeyHook Create(Hotkey mapping, IColorConsole console)
    {
        var hook = GlobalHotkeyHookFactory.Create(console, _config, _shellHost);
        hook.SetHotkey(mapping);
        return hook;
    }
}

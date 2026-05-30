using ArayCode.VisualFeedback;

namespace ArayCode.Services;

internal sealed class PttController : IPttController
{
    private readonly IHotkeyHookFactory? _hotkeyHookFactory;
    private readonly IColorConsole? _console;
    private readonly AppConfig? _config;
    private readonly IStreamShellHost? _shellHost;
    private IGlobalHotkeyHook? _hotkeyHook;
    private bool _disposed;

    private volatile bool _externalHotkeyPressed;
    private volatile bool _externalHotkeyRelease;
    private volatile bool _cancelRecording;

    public PttController(IHotkeyHookFactory? hotkeyHookFactory = null, IColorConsole? console = null,
        AppConfig? config = null, IStreamShellHost? shellHost = null)
    {
        _hotkeyHookFactory = hotkeyHookFactory;
        _console = console;
        _config = config;
        _shellHost = shellHost;
    }

    public void SetHotkey(string hotkeyCombination, bool holdToTalk)
    {
        _hotkeyHook?.Dispose();

        var mapping = HotkeyMapping.Parse(hotkeyCombination);

        if (_hotkeyHookFactory != null && _console != null)
        {
            _hotkeyHook = _hotkeyHookFactory.Create(mapping, _console);
        }
        else if (_console != null)
        {
            _hotkeyHook = GlobalHotkeyHookFactory.Create(_console, _config, _shellHost);
            _hotkeyHook.SetHotkey(mapping);
        }
        else
        {
            throw new InvalidOperationException("IColorConsole is required to create hotkey hook.");
        }
        
        _hotkeyHook.Start();


    }

    public bool PollHotkeyPressed()
    {
        if (_externalHotkeyPressed)
        {
            _externalHotkeyPressed = false;
            return true;
        }
        return false;
    }

    public bool PollHotkeyRelease()
    {
        if (_externalHotkeyRelease)
        {
            _externalHotkeyRelease = false;
            return true;
        }
        return false;
    }

    public void StartRecording()
    {
        _externalHotkeyPressed = true;
        _externalHotkeyRelease = false;
    }

    public void StopRecording()
    {
        _externalHotkeyPressed = false;
        _externalHotkeyRelease = true;
    }

    public void CancelRecording()
    {
        _cancelRecording = true;
        _externalHotkeyPressed = false;
        _externalHotkeyRelease = false;
    }

    /// <summary>Returns true if the current recording should be cancelled (Escape pressed).</summary>
    public bool PollCancelRecording()
    {
        if (_cancelRecording)
        {
            _cancelRecording = false;
            return true;
        }
        return false;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _hotkeyHook?.Dispose();
            _disposed = true;
        }
    }
}

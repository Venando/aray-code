using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace ArayCode;

[SupportedOSPlatform("windows")]
internal sealed class WindowsHotkeyHook : IGlobalHotkeyHook
{
    public event Action? HotkeyPressed;
    public event Action? HotkeyReleased;
    public event Action<int>? HotkeyIndexPressed;
    public event Action<int>? HotkeyIndexReleased;
    public event Action? EscapePressed;

    private const int VK_ESCAPE = 0x1B;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private volatile IntPtr _hookHandle = IntPtr.Zero;
    private volatile bool _disposed;
    
    // Hotkey configuration
    private List<HotkeyConfig> _hotkeyConfigs = new();
    private Dictionary<Modifier, List<int>> _modifierKeyCodes = new();
    private Dictionary<Modifier, bool> _modifierDown = new();
    private int _activeHotkeyIndex = -1;
    private volatile bool _blockEscape;

    private sealed record HotkeyConfig(int KeyCode, HashSet<Modifier> Modifiers);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;

    // Virtual key codes
    private const int VK_LMENU = 0xA4;      // Left Alt
    private const int VK_RMENU = 0xA5;      // Right Alt
    private const int VK_LCONTROL = 0xA2;   // Left Ctrl
    private const int VK_RCONTROL = 0xA3;   // Right Ctrl
    private const int VK_LSHIFT = 0xA0;     // Left Shift
    private const int VK_RSHIFT = 0xA1;     // Right Shift
    private const int VK_LWIN = 0x5B;       // Left Windows
    private const int VK_RWIN = 0x5C;       // Right Windows

    public WindowsHotkeyHook()
    {
        _thread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "WinHotkeyLoop"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        
        // Initialize modifier mapping
        _modifierKeyCodes[Modifier.Alt] = new List<int> { VK_LMENU, VK_RMENU };
        _modifierKeyCodes[Modifier.Ctrl] = new List<int> { VK_LCONTROL, VK_RCONTROL };
        _modifierKeyCodes[Modifier.Shift] = new List<int> { VK_LSHIFT, VK_RSHIFT };
        _modifierKeyCodes[Modifier.Win] = new List<int> { VK_LWIN, VK_RWIN };
        foreach (var mod in Enum.GetValues<Modifier>())
            _modifierDown[mod] = false;
    }

    public void SetHotkey(Hotkey hotkey)
    {
        SetHotkeys(new[] { hotkey });
    }

    public void SetHotkeys(System.Collections.Generic.IEnumerable<Hotkey> hotkeys)
    {
        _hotkeyConfigs = hotkeys
            .Select(h => new HotkeyConfig(HotkeyMapping.GetPlatformKeyCode(h.Key), h.Modifiers))
            .ToList();
        // Reset states
        foreach (var mod in Enum.GetValues<Modifier>())
            _modifierDown[mod] = false;
        _activeHotkeyIndex = -1;
    }

    public bool BlockEscape
    {
        get => _blockEscape;
        set => _blockEscape = value;
    }

    public void Start()
    {
        _thread.Start();
        // Block until the hook is actually installed, so callers know
        // that key events will be intercepted from this point on.
        _ready.Wait();
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _hotkeyConfigs.Count > 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int msg = wParam.ToInt32();

            bool isDown = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
            bool isUp = msg is WM_KEYUP or WM_SYSKEYUP;

            // Update modifier states
            foreach (var (mod, vkList) in _modifierKeyCodes)
            {
                if (vkList.Contains((int)info.vkCode))
                {
                    _modifierDown[mod] = isDown;
                    break;
                }
            }

            // Check for Escape key (cancel recording)
            if (info.vkCode == VK_ESCAPE && isDown)
            {
                if (_blockEscape)
                {
                    ThreadPool.QueueUserWorkItem(_ => EscapePressed?.Invoke());
                    return new IntPtr(1);
                }
                // When not recording, let Escape pass through to StreamShell for input clearing
            }

            // Check all configured hotkeys
            int matchedIndex = FindMatchingHotkeyIndex((int)info.vkCode);
            if (isDown && matchedIndex >= 0 && _activeHotkeyIndex < 0)
            {
                _activeHotkeyIndex = matchedIndex;
                int capturedIndex = matchedIndex;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    HotkeyPressed?.Invoke();
                    HotkeyIndexPressed?.Invoke(capturedIndex);
                });
                // Block the keystroke so it doesn't reach the console/StreamShell
                return new IntPtr(1);
            }
            else if (isUp && _activeHotkeyIndex >= 0)
            {
                // Fire release when the active hotkey's key goes up.
                // Use the saved index rather than re-matching — modifiers may
                // already be released, causing FindMatchingHotkeyIndex to miss
                // and _activeHotkeyIndex to stick.
                var activeHotkey = _activeHotkeyIndex < _hotkeyConfigs.Count
                    ? _hotkeyConfigs[_activeHotkeyIndex]
                    : null;
                if (activeHotkey != null && activeHotkey.KeyCode == (int)info.vkCode)
                {
                    int capturedIndex = _activeHotkeyIndex;
                    _activeHotkeyIndex = -1;
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        HotkeyReleased?.Invoke();
                        HotkeyIndexReleased?.Invoke(capturedIndex);
                    });
                    // Block the keystroke release too
                    return new IntPtr(1);
                }
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private int FindMatchingHotkeyIndex(int vkCode)
    {
        for (int i = 0; i < _hotkeyConfigs.Count; i++)
        {
            var cfg = _hotkeyConfigs[i];
            if (cfg.KeyCode == vkCode && ModifiersMatch(cfg.Modifiers))
                return i;
        }
        return -1;
    }

    private bool ModifiersMatch(HashSet<Modifier> modifiers)
    {
        foreach (var mod in Enum.GetValues<Modifier>())
        {
            bool shouldBePressed = modifiers.Contains(mod);
            bool isPressed = _modifierDown[mod];
            if (shouldBePressed != isPressed)
                return false;
        }
        return true;
    }

    private void MessageLoop()
    {
        using var proc = Process.GetCurrentProcess();
        using var module = proc.MainModule!;

        LowLevelKeyboardProc cb = HookProc;   // keep delegate rooted
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, cb,
            GetModuleHandle(module.ModuleName), 0);

        if (_hookHandle == IntPtr.Zero)
        {
            _ready.Set(); // unblock caller so it can handle the error
            throw new InvalidOperationException(
                $"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
        }

        _ready.Set(); // hook is installed — unblock Start()

        while (!_disposed && GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        UnhookWindowsHookEx(_hookHandle);
    }

    public void Dispose()
    {
        _disposed = true;
        PostThreadMessage((uint)_thread.ManagedThreadId, 0x0012 /*WM_QUIT*/,
            IntPtr.Zero, IntPtr.Zero);
    }

    // ── P/Invoke ──────────────────────────────────────────────────────

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam, time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hWnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr w, IntPtr l);
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using ArayCode.Services;

namespace ArayCode;

/// <summary>
/// Listens for global hotkeys via X11 XGrabKey + XNextEvent.
/// Used when evdev (/dev/input/event*) is not accessible.
/// Requires a running X11 server (DISPLAY env var).
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxX11HotkeyHook : IGlobalHotkeyHook
{
    public event Action? HotkeyPressed;
    public event Action? HotkeyReleased;
    public event Action<int>? HotkeyIndexPressed;
    public event Action<int>? HotkeyIndexReleased;
    public event Action? EscapePressed;

    private bool _blockEscape;
    public bool BlockEscape
    {
        get => _blockEscape;
        set
        {
            if (value == _blockEscape) return;
            _blockEscape = value;
            if (_display != IntPtr.Zero)
            {
                if (value) GrabEscapeKey();
                else UngrabEscapeKey();
                XFlush(_display);
            }
        }
    }

    private readonly IColorConsole _console;
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;
    private List<Hotkey> _hotkeys = new();
    private int _activeHotkeyIndex = -1;

    // X11 state
    private IntPtr _display = IntPtr.Zero;
    private IntPtr _rootWindow = IntPtr.Zero;
    private int _escapeKeycode;
    private int[] _hotkeyKeycodes = Array.Empty<int>();

    // ── X11 constants ────────────────────────────────────────────────

    // Event types (from X.h)
    private const int KeyPress = 2;
    private const int KeyRelease = 3;

    // Modifier masks (from X.h)
    private const uint ShiftMask = 0x01;
    private const uint LockMask = 0x02;
    private const uint ControlMask = 0x04;
    private const uint Mod1Mask = 0x08;    // Alt
    private const uint Mod2Mask = 0x10;    // NumLock
    private const uint Mod4Mask = 0x40;    // Super/Win
    private const uint IgnoredModifiers = LockMask | Mod2Mask;
    private const int GrabModeAsync = 1;   // from X.h

    // Keysyms
    private const int XK_Escape = 0xFF1B;

    // Offsets within XKeyEvent on x86-64 Linux (24-byte padded to 8)
    private const int EventTypeOffset = 0;
    private const int EventStateOffset = 80;
    private const int EventKeycodeOffset = 84;

    // sizeof(XEvent) on x86-64 = long pad[24] = 192 bytes
    private const int XEventSize = 192;

    public LinuxX11HotkeyHook(IColorConsole console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    public void SetHotkey(Hotkey hotkey) => SetHotkeys(new[] { hotkey });

    public void SetHotkeys(IEnumerable<Hotkey> hotkeys)
    {
        _hotkeys = hotkeys.ToList();
        _activeHotkeyIndex = -1;
    }

    public void Start()
    {
        _thread = new Thread(ReadLoop) { IsBackground = true, Name = "X11HotkeyLoop" };
        _thread.Start();
    }

    // ── event loop ───────────────────────────────────────────────────

    private void ReadLoop()
    {
        _display = XOpenDisplay(IntPtr.Zero);
        if (_display == IntPtr.Zero)
        {
            _console.PrintWarning("Hotkey. X11 display not available. Hotkeys will not work.");
            return;
        }

        _rootWindow = XRootWindow(_display, XDefaultScreen(_display));
        InitializeGrabbedKeys();

        if (_blockEscape)
            GrabEscapeKey();

        XFlush(_display);

        var eventBuf = new byte[XEventSize];
        var handle = GCHandle.Alloc(eventBuf, GCHandleType.Pinned);
        var ct = _cts.Token;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Poll for pending events to avoid blocking indefinitely on XNextEvent
                while (XPending(_display) > 0 && !ct.IsCancellationRequested)
                {
                    XNextEvent(_display, handle.AddrOfPinnedObject());
                    ProcessEvent(eventBuf);
                }

                if (!ct.IsCancellationRequested)
                    Thread.Sleep(10);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ThreadInterruptedException)
        {
            _console.Log("hotkey", $"X11 event loop error: {ex.Message}");
        }
        finally
        {
            handle.Free();
            CleanupX11();
        }
    }

    private void ProcessEvent(byte[] eventBuf)
    {
        int type = BitConverter.ToInt32(eventBuf, EventTypeOffset);

        if (type == KeyPress)
        {
            uint keycode = BitConverter.ToUInt32(eventBuf, EventKeycodeOffset);
            uint state = BitConverter.ToUInt32(eventBuf, EventStateOffset);

            // Check for Escape
            if (keycode == _escapeKeycode && _blockEscape)
            {
                ThreadPool.QueueUserWorkItem(_ => EscapePressed?.Invoke());
                return;
            }

            // Match against configured hotkeys
            uint relevantMods = state & ~IgnoredModifiers;
            int matchedIndex = FindMatchingHotkeyIndex((int)keycode, relevantMods);

            if (matchedIndex >= 0 &&
                Interlocked.CompareExchange(ref _activeHotkeyIndex, matchedIndex, -1) == -1)
            {
                int captured = matchedIndex;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    HotkeyPressed?.Invoke();
                    HotkeyIndexPressed?.Invoke(captured);
                });
            }
        }
        else if (type == KeyRelease)
        {
            uint keycode = BitConverter.ToUInt32(eventBuf, EventKeycodeOffset);

            if (_activeHotkeyIndex >= 0 && _activeHotkeyIndex < _hotkeyKeycodes.Length)
            {
                int hotkeyKeycode = _hotkeyKeycodes[_activeHotkeyIndex];
                if (keycode == hotkeyKeycode)
                {
                    int captured = _activeHotkeyIndex;
                    _activeHotkeyIndex = -1;
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        HotkeyReleased?.Invoke();
                        HotkeyIndexReleased?.Invoke(captured);
                    });
                }
            }
        }
    }

    // ── X11 grab management ──────────────────────────────────────────

    private void InitializeGrabbedKeys()
    {
        _hotkeyKeycodes = new int[_hotkeys.Count];

        for (int i = 0; i < _hotkeys.Count; i++)
        {
            var hk = _hotkeys[i];
            int keysym = GetKeysym(hk.Key);
            int keycode = XKeysymToKeycode(_display, (IntPtr)keysym);
            _hotkeyKeycodes[i] = keycode;

            uint baseMods = GetModifierMask(hk.Modifiers);

            // Grab the key combination with all inert modifier variants
            // so it works regardless of CapsLock / NumLock state.
            var inertVariants = new[] { 0u, LockMask, Mod2Mask, LockMask | Mod2Mask };
            foreach (var inert in inertVariants)
                XGrabKey(_display, keycode, (int)(baseMods | inert), _rootWindow,
                    0, GrabModeAsync, GrabModeAsync);
        }
    }

    private void GrabEscapeKey()
    {
        _escapeKeycode = XKeysymToKeycode(_display, (IntPtr)XK_Escape);
        var inertVariants = new[] { 0u, LockMask, Mod2Mask, LockMask | Mod2Mask };
        foreach (var inert in inertVariants)
            XGrabKey(_display, _escapeKeycode, (int)inert, _rootWindow,
                0, GrabModeAsync, GrabModeAsync);
    }

    private void UngrabEscapeKey()
    {
        var inertVariants = new[] { 0u, LockMask, Mod2Mask, LockMask | Mod2Mask };
        foreach (var inert in inertVariants)
            XUngrabKey(_display, _escapeKeycode, (int)inert, _rootWindow);
    }

    // ── matching ─────────────────────────────────────────────────────

    private int FindMatchingHotkeyIndex(int keycode, uint mods)
    {
        for (int i = 0; i < _hotkeys.Count; i++)
        {
            if (_hotkeyKeycodes[i] != keycode) continue;
            uint expectedMods = GetModifierMask(_hotkeys[i].Modifiers);
            if (mods == expectedMods)
                return i;
        }
        return -1;
    }

    private static int GetKeysym(Key key)
    {
        if (key.Special == SpecialKey.None)
        {
            char c = key.Value;
            // ASCII keysym: letters and digits
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                return c;
            if (c >= 'a' && c <= 'z')
                return c;
            throw new NotSupportedException($"Key '{c}' not supported on X11");
        }

        return key.Special switch
        {
            SpecialKey.Space => 0x0020,
            SpecialKey.Equal => 0x003D,
            SpecialKey.Minus => 0x002D,
            SpecialKey.F1 => 0xFFBE,
            SpecialKey.F2 => 0xFFBF,
            SpecialKey.F3 => 0xFFC0,
            SpecialKey.F4 => 0xFFC1,
            SpecialKey.F5 => 0xFFC2,
            SpecialKey.F6 => 0xFFC3,
            SpecialKey.F7 => 0xFFC4,
            SpecialKey.F8 => 0xFFC5,
            SpecialKey.F9 => 0xFFC6,
            SpecialKey.F10 => 0xFFC7,
            SpecialKey.F11 => 0xFFC8,
            SpecialKey.F12 => 0xFFC9,
            _ => throw new NotSupportedException($"Special key {key.Special} not supported on X11")
        };
    }

    private static uint GetModifierMask(HashSet<Modifier> modifiers)
    {
        uint mask = 0;
        foreach (var mod in modifiers)
        {
            mask |= mod switch
            {
                Modifier.Alt => Mod1Mask,
                Modifier.Ctrl => ControlMask,
                Modifier.Shift => ShiftMask,
                Modifier.Win => Mod4Mask,
                _ => 0
            };
        }
        return mask;
    }

    // ── cleanup ──────────────────────────────────────────────────────

    private void CleanupX11()
    {
        if (_display == IntPtr.Zero) return;

        // Ungrab all hotkeys
        for (int i = 0; i < _hotkeyKeycodes.Length; i++)
        {
            int keycode = _hotkeyKeycodes[i];
            if (keycode <= 0) continue;

            uint baseMods = i < _hotkeys.Count ? GetModifierMask(_hotkeys[i].Modifiers) : 0;
            var inertVariants = new[] { 0u, LockMask, Mod2Mask, LockMask | Mod2Mask };
            foreach (var inert in inertVariants)
                XUngrabKey(_display, keycode, (int)(baseMods | inert), _rootWindow);
        }

        if (_blockEscape && _escapeKeycode > 0)
            UngrabEscapeKey();

        XCloseDisplay(_display);
        _display = IntPtr.Zero;
    }

    public void Dispose()
    {
        _cts.Cancel();
    }

    // ── P/Invoke into libX11.so.6 ────────────────────────────────────

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XRootWindow(IntPtr display, int screenNumber);

    [DllImport("libX11.so.6")]
    private static extern int XDefaultScreen(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XKeysymToKeycode(IntPtr display, IntPtr keysym);

    [DllImport("libX11.so.6")]
    private static extern int XGrabKey(IntPtr display, int keycode, int modifiers,
        IntPtr grabWindow, int ownerEvents, int pointerMode, int keyboardMode);

    [DllImport("libX11.so.6")]
    private static extern int XUngrabKey(IntPtr display, int keycode, int modifiers,
        IntPtr grabWindow);

    [DllImport("libX11.so.6")]
    private static extern int XNextEvent(IntPtr display, IntPtr eventReturn);

    [DllImport("libX11.so.6")]
    private static extern int XPending(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);
}

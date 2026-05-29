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
    private volatile int _pendingEscapeGrab; // 1 = grab, -1 = ungrab, 0 = no change

    public bool BlockEscape
    {
        get => _blockEscape;
        set
        {
            if (value == _blockEscape) return;
            _blockEscape = value;
            if (_display != IntPtr.Zero)
            {
                // Xlib is not thread-safe. We cannot call XGrabKey/XUngrabKey/XFlush
                // here because this setter may execute on any thread. Post the request
                // so the X11 event loop thread picks it up at the next iteration.
                Volatile.Write(ref _pendingEscapeGrab, value ? 1 : -1);
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

    // X11 error handler delegate — must be kept alive for the lifetime of the hook
    // to prevent the GC from collecting it while native code holds a reference.
    private XErrorHandlerDelegate? _errorHandler;
    private delegate int XErrorHandlerDelegate(IntPtr display, ref XErrorEvent ev);

    // ── X11 constants ────────────────────────────────────────────────

    private const int KeyPress = 2;
    private const int KeyRelease = 3;
    private const int ClientMessage = 33;

    private const uint ShiftMask = 0x01;
    private const uint LockMask = 0x02;
    private const uint ControlMask = 0x04;
    private const uint Mod1Mask = 0x08;
    private const uint Mod2Mask = 0x10;
    private const uint Mod4Mask = 0x40;
    private const uint IgnoredModifiers = LockMask | Mod2Mask;
    private const int GrabModeAsync = 1;

    private const int XK_Escape = 0xFF1B;

    // Offsets within XKeyEvent on x86-64 Linux:
    //   type(int,0)  serial(ulong,8)  send_event(int,16)  _pad(4)
    //   display(ptr,24)  window(ulong,32)  root(ulong,40)  subwindow(ulong,48)
    //   time(ulong,56)  x(int,64)  y(int,68)  x_root(int,72)  y_root(int,76)
    //   state(uint,80)  keycode(uint,84)
    private const int EventTypeOffset = 0;
    private const int EventStateOffset = 80;
    private const int EventKeycodeOffset = 84;

    private const int XEventSize = 192;

    // ClientMessage atom used as a wakeup sentinel to unblock XNextEvent on Dispose.
    private IntPtr _wakeAtom = IntPtr.Zero;

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
        _console.Log("hotkey", $"[diag] ReadLoop started. DISPLAY={Environment.GetEnvironmentVariable("DISPLAY")}");

        // Install error handler before any Xlib calls so grab failures are visible.
        // Default Xlib error handler prints to stderr and continues; we log instead.
        _errorHandler = OnX11Error;
        XSetErrorHandler(_errorHandler);
        _console.Log("hotkey", "[diag] X11 error handler installed");

        _display = XOpenDisplay(IntPtr.Zero);
        _console.Log("hotkey", $"[diag] XOpenDisplay → 0x{_display:X}");
        if (_display == IntPtr.Zero)
        {
            _console.PrintWarning("Hotkey. X11 display not available. Hotkeys will not work.");
            _console.Log("hotkey", "[diag] DISPLAY env var may be unset or X server not running");
            return;
        }

        int screen = XDefaultScreen(_display);
        _rootWindow = XRootWindow(_display, screen);
        _console.Log("hotkey", $"[diag] screen={screen}  rootWindow=0x{_rootWindow:X}");

        // Intern a private atom used as the ClientMessage wakeup sentinel.
        _wakeAtom = XInternAtom(_display, "_ARAYCODE_HOTKEY_WAKE", 0);
        _console.Log("hotkey", $"[diag] wakeAtom=0x{_wakeAtom:X}");

        InitializeGrabbedKeys();
        if (_blockEscape)
            GrabEscapeKey();

        // Flush + sync so all XGrabKey requests reach the server and any BadAccess
        // errors are delivered before we enter the blocking XNextEvent loop.
        XFlush(_display);
        XSync(_display, 0);
        _console.Log("hotkey", "[diag] XFlush+XSync done — entering XNextEvent loop");

        var eventBuf = new byte[XEventSize];
        var handle = GCHandle.Alloc(eventBuf, GCHandleType.Pinned);
        var ct = _cts.Token;
        // Snapshot for CleanupX11 — see comment there.
        IntPtr displaySnapshot = _display;

        _console.LogOk("hotkey", "X11 hotkey hook started");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Apply any pending BlockEscape change posted from another thread.
                int pending = Interlocked.Exchange(ref _pendingEscapeGrab, 0);
                if (pending == 1) { GrabEscapeKey(); XFlush(_display); XSync(_display, 0); }
                else if (pending == -1) { UngrabEscapeKey(); XFlush(_display); XSync(_display, 0); }

                _console.Log("hotkey", "[diag] calling XNextEvent — blocked until a grabbed key fires");
                XNextEvent(_display, handle.AddrOfPinnedObject());
                _console.Log("hotkey", "[diag] XNextEvent returned");

                if (ct.IsCancellationRequested)
                {
                    _console.Log("hotkey", "[diag] cancellation requested after XNextEvent — exiting loop");
                    break;
                }

                int evType = BitConverter.ToInt32(eventBuf, EventTypeOffset);
                _console.Log("hotkey", $"[diag] event type={evType}");

                // Wakeup sentinel sent by Dispose() — skip processing.
                if (evType == ClientMessage)
                {
                    _console.Log("hotkey", "[diag] ClientMessage wakeup received — exiting loop");
                    break;
                }

                ProcessEvent(eventBuf);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ThreadInterruptedException)
        {
            // XNextEvent throws (SEHException / AccessViolationException) when
            // Dispose() closes the display from another thread. If it was intentional
            // we log at trace level; otherwise surface it.
            _console.Log("hotkey", $"[diag] exception in event loop: {ex.GetType().Name}: {ex.Message}");
            if (!ct.IsCancellationRequested)
                _console.Log("hotkey", $"X11 event loop error (unexpected): {ex.Message}");
        }
        finally
        {
            _console.Log("hotkey", "[diag] ReadLoop finally — freeing GCHandle and cleaning up");
            handle.Free();
            CleanupX11(displaySnapshot);
            _console.Log("hotkey", "[diag] ReadLoop exited cleanly");
        }
    }

    private int OnX11Error(IntPtr display, ref XErrorEvent ev)
    {
        // error_code 10 = BadAccess — another client already owns this grab.
        // error_code  3 = BadWindow — root window handle is stale.
        string meaning = ev.error_code switch
        {
            10 => "BadAccess — another client already holds this grab",
            3 => "BadWindow — invalid window handle",
            _ => ""
        };
        _console.Log("hotkey",
            $"[diag] X11 error: serial={ev.serial} error_code={ev.error_code} " +
            $"request_code={ev.request_code} minor_code={ev.minor_code} " +
            $"resourceid=0x{ev.resourceid:X}" +
            (meaning.Length > 0 ? $" — {meaning}" : ""));
        return 0; // must return 0; non-zero is undefined per Xlib spec
    }

    private void ProcessEvent(byte[] eventBuf)
    {
        int type = BitConverter.ToInt32(eventBuf, EventTypeOffset);

        if (type == KeyPress)
        {
            uint keycode = BitConverter.ToUInt32(eventBuf, EventKeycodeOffset);
            uint state = BitConverter.ToUInt32(eventBuf, EventStateOffset);
            _console.Log("hotkey", $"[diag] KeyPress keycode={keycode} state=0x{state:X}");

            if (keycode == _escapeKeycode && _blockEscape)
            {
                _console.Log("hotkey", "[diag] → Escape fired");
                ThreadPool.QueueUserWorkItem(_ => EscapePressed?.Invoke());
                return;
            }

            uint relevantMods = state & ~IgnoredModifiers;
            int matchedIndex = FindMatchingHotkeyIndex((int)keycode, relevantMods);
            _console.Log("hotkey", $"[diag] relevantMods=0x{relevantMods:X}  matchedIndex={matchedIndex}");

            if (matchedIndex >= 0 &&
                Interlocked.CompareExchange(ref _activeHotkeyIndex, matchedIndex, -1) == -1)
            {
                _console.Log("hotkey", $"[diag] → HotkeyPressed index={matchedIndex}");
                int captured = matchedIndex;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    HotkeyPressed?.Invoke();
                    HotkeyIndexPressed?.Invoke(captured);
                });
            }
            else
            {
                _console.Log("hotkey", $"[diag] KeyPress not matched or already active (activeIndex={_activeHotkeyIndex})");
            }
        }
        else if (type == KeyRelease)
        {
            uint keycode = BitConverter.ToUInt32(eventBuf, EventKeycodeOffset);
            _console.Log("hotkey", $"[diag] KeyRelease keycode={keycode}  activeIndex={_activeHotkeyIndex}");

            if (_activeHotkeyIndex >= 0 && _activeHotkeyIndex < _hotkeyKeycodes.Length)
            {
                int hotkeyKeycode = _hotkeyKeycodes[_activeHotkeyIndex];
                if (keycode == hotkeyKeycode)
                {
                    int captured = _activeHotkeyIndex;
                    _activeHotkeyIndex = -1;
                    _console.Log("hotkey", $"[diag] → HotkeyReleased index={captured}");
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        HotkeyReleased?.Invoke();
                        HotkeyIndexReleased?.Invoke(captured);
                    });
                }
                else
                {
                    _console.Log("hotkey", $"[diag] KeyRelease keycode={keycode} != hotkeyKeycode={hotkeyKeycode} — ignoring");
                }
            }
        }
        else
        {
            _console.Log("hotkey", $"[diag] unhandled event type={type} — ignoring");
        }
    }

    // ── X11 grab management ──────────────────────────────────────────

    private void InitializeGrabbedKeys()
    {
        _console.Log("hotkey", $"[diag] InitializeGrabbedKeys — {_hotkeys.Count} hotkey(s)");
        _hotkeyKeycodes = new int[_hotkeys.Count];

        for (int i = 0; i < _hotkeys.Count; i++)
        {
            var hk = _hotkeys[i];
            int keysym = GetKeysym(hk.Key);
            int keycode = XKeysymToKeycode(_display, (IntPtr)keysym);
            _hotkeyKeycodes[i] = keycode;

            _console.Log("hotkey",
                $"[diag]   [{i}] key={hk.Key}  keysym=0x{keysym:X}  keycode={keycode}  " +
                $"mods=[{string.Join(",", hk.Modifiers)}]");

            if (keycode <= 0)
            {
                _console.Log("hotkey",
                    $"[diag]   [{i}] *** XKeysymToKeycode returned 0 — XGrabKey skipped. " +
                    $"The key will never fire. Check that keysym 0x{keysym:X} exists in " +
                    $"your current keyboard layout (run: xmodmap -pke | grep {keysym:x})");
                continue;
            }

            uint baseMods = GetModifierMask(hk.Modifiers);
            _console.Log("hotkey", $"[diag]   [{i}] baseMods=0x{baseMods:X} — calling XGrabKey x4 variants");

            var inertVariants = new[] { 0u, LockMask, Mod2Mask, LockMask | Mod2Mask };
            foreach (var inert in inertVariants)
            {
                int result = XGrabKey(_display, keycode, (int)(baseMods | inert), _rootWindow,
                    0, GrabModeAsync, GrabModeAsync);
                // XGrabKey returns 1 on success; errors are delivered async via the
                // error handler (BadAccess = another client owns this grab).
                _console.Log("hotkey", $"[diag]     XGrabKey(mods=0x{baseMods | inert:X}) → {result}");
            }

            _console.LogOk("hotkey", $"Grabbed hotkey {hk} — keycode={keycode} mods=0x{baseMods:X}");
        }
    }

    private void GrabEscapeKey()
    {
        _escapeKeycode = XKeysymToKeycode(_display, (IntPtr)XK_Escape);
        _console.Log("hotkey", $"[diag] GrabEscapeKey — escapeKeycode={_escapeKeycode}");
        var inertVariants = new[] { 0u, LockMask, Mod2Mask, LockMask | Mod2Mask };
        foreach (var inert in inertVariants)
            XGrabKey(_display, _escapeKeycode, (int)inert, _rootWindow,
                0, GrabModeAsync, GrabModeAsync);
    }

    private void UngrabEscapeKey()
    {
        _console.Log("hotkey", $"[diag] UngrabEscapeKey — escapeKeycode={_escapeKeycode}");
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
            _console.Log("hotkey", $"[diag]   candidate [{i}]: expectedMods=0x{expectedMods:X} vs actual=0x{mods:X}");
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
            // X11 keysyms for printable Latin characters match their Unicode/ASCII
            // codepoints, but the canonical keysym for a letter key is always the
            // LOWERCASE form (0x61–0x7A). Passing an uppercase codepoint (0x41–0x5A)
            // maps to a different, usually absent keysym and XKeysymToKeycode returns 0.
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                return char.ToLowerInvariant(c);
            if (c >= '0' && c <= '9')
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

    // displaySnapshot is the display handle captured in ReadLoop before the event
    // loop started. Dispose() may have already closed it (and zeroed _display via
    // Interlocked.Exchange). We use a CAS to determine ownership:
    //   - If we can atomically zero _display from displaySnapshot → we own it and
    //     do a full ungrab + close.
    //   - If _display is already IntPtr.Zero → Dispose closed it; server released
    //     all grabs automatically on disconnect. Nothing to do.
    private void CleanupX11(IntPtr displaySnapshot)
    {
        _console.Log("hotkey", $"[diag] CleanupX11 — displaySnapshot=0x{displaySnapshot:X}");
        if (displaySnapshot == IntPtr.Zero) return;

        // Try to take ownership. If CAS fails, Dispose already zeroed+closed it.
        if (Interlocked.CompareExchange(ref _display, IntPtr.Zero, displaySnapshot) != displaySnapshot)
        {
            _console.Log("hotkey", "[diag] CleanupX11 — Dispose already closed the display, skipping");
            return;
        }

        _console.Log("hotkey", $"[diag] CleanupX11 — ungrabbing {_hotkeyKeycodes.Length} key(s)");
        for (int i = 0; i < _hotkeyKeycodes.Length; i++)
        {
            int keycode = _hotkeyKeycodes[i];
            if (keycode <= 0) continue;

            uint baseMods = i < _hotkeys.Count ? GetModifierMask(_hotkeys[i].Modifiers) : 0;
            var inertVariants = new[] { 0u, LockMask, Mod2Mask, LockMask | Mod2Mask };
            foreach (var inert in inertVariants)
                XUngrabKey(displaySnapshot, keycode, (int)(baseMods | inert), _rootWindow);
        }

        if (_blockEscape && _escapeKeycode > 0)
        {
            _console.Log("hotkey", "[diag] CleanupX11 — ungrabbing Escape");
            var inertVariants = new[] { 0u, LockMask, Mod2Mask, LockMask | Mod2Mask };
            foreach (var inert in inertVariants)
                XUngrabKey(displaySnapshot, _escapeKeycode, (int)inert, _rootWindow);
        }

        _console.Log("hotkey", "[diag] CleanupX11 — XCloseDisplay");
        XCloseDisplay(displaySnapshot);
    }

    public void Dispose()
    {
        _console.Log("hotkey", "[diag] Dispose called — cancelling CTS");
        _cts.Cancel();

        // Send a ClientMessage to ourselves to unblock XNextEvent cleanly.
        // Calling XCloseDisplay from a different thread while XNextEvent is blocking
        // is undefined behavior — Xlib's default I/O error handler calls exit(),
        // which kills the process without unwinding the stack (no catch, no finally).
        var display = _display;
        var rootWindow = _rootWindow;
        if (display != IntPtr.Zero && rootWindow != IntPtr.Zero && _wakeAtom != IntPtr.Zero)
        {
            _console.Log("hotkey", "[diag] Dispose — sending ClientMessage wakeup to XNextEvent");
            var ev = new XClientMessageEvent
            {
                type = ClientMessage,
                send_event = 1,
                display = display,
                window = rootWindow,
                message_type = _wakeAtom,
                format = 32,
            };
            XSendEvent(display, rootWindow, 0, 0, ref ev);
            XFlush(display);
        }
        else
        {
            // Fallback: if wakeAtom isn't set yet (Dispose called before ReadLoop
            // fully initialised), close the display directly. This is the original
            // racy path but it's only hit during very early disposal.
            _console.Log("hotkey", "[diag] Dispose — wakeAtom not ready, falling back to XCloseDisplay");
            var d = Interlocked.Exchange(ref _display, IntPtr.Zero);
            if (d != IntPtr.Zero)
                XCloseDisplay(d);
        }
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
    private static extern int XFlush(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XSync(IntPtr display, int discard);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XSetErrorHandler(XErrorHandlerDelegate handler);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XInternAtom(IntPtr display, string atomName, int onlyIfExists);

    [DllImport("libX11.so.6")]
    private static extern int XSendEvent(IntPtr display, IntPtr window, int propagate,
        long eventMask, ref XClientMessageEvent ev);

    // ── P/Invoke structs ─────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct XErrorEvent
    {
        public int type;
        public IntPtr display;
        public ulong resourceid;
        public ulong serial;
        public byte error_code;
        public byte request_code;
        public byte minor_code;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XClientMessageEvent
    {
        public int type;
        public ulong serial;
        public int send_event;
        public IntPtr display;
        public IntPtr window;
        public IntPtr message_type;
        public int format;
        // data union — 5 longs (40 bytes on 64-bit)
        public long data0, data1, data2, data3, data4;
    }
}
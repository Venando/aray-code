using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ArayCode.Services;

namespace ArayCode;

[SupportedOSPlatform("macos")]
internal sealed class MacOsHotkeyHook : IGlobalHotkeyHook
{
    public event Action? HotkeyPressed;
    public event Action? HotkeyReleased;
#pragma warning disable CS0067 // Event is part of interface but not used on macOS
    public event Action<int>? HotkeyIndexPressed;
    public event Action<int>? HotkeyIndexReleased;
    public event Action? EscapePressed;
#pragma warning restore CS0067
    public bool BlockEscape { get; set; }

    private readonly CancellationTokenSource _cts = new();
    private readonly IColorConsole _console;
    private Thread? _thread;
    
    // Hotkey configuration
    private Hotkey? _hotkey;
    private long _hotkeyKeyCode;
    private ulong _modifierFlagsMask;
    private bool _hotkeyKeyDown;

    // CGEventType
    private const int kCGEventKeyDown = 10;
    private const int kCGEventKeyUp = 11;
    private const int kCGEventFlagsChanged = 12;

    private GCHandle _selfHandle;   // keeps 'this' rooted while callback is alive
    private IntPtr _eventTap;
    private IntPtr _runLoopSource;

    public MacOsHotkeyHook(IColorConsole console)
    {
        _console = console;
    }

    public void SetHotkey(Hotkey hotkey)
    {
        _hotkey = hotkey;
        _hotkeyKeyCode = HotkeyMapping.GetPlatformKeyCode(hotkey.Key);
        _modifierFlagsMask = HotkeyMapping.GetPlatformModifierFlags(hotkey.Modifiers);
        _hotkeyKeyDown = false;
    }

    public void SetHotkeys(System.Collections.Generic.IEnumerable<Hotkey> hotkeys)
    {
        // macOS single-hotkey hook — use first for now
        foreach (var hk in hotkeys)
        {
            SetHotkey(hk);
            break;
        }
    }

    public void Start()
    {
        _thread = new Thread(RunLoopThread) { IsBackground = true, Name = "MacHotkeyLoop" };
        _thread.Start();
    }

    private void RunLoopThread()
    {
        // Check permission first — tap will be created but dead without it
        if (!AXIsProcessTrusted())
        {
            _console.Log("hotkey", "Accessibility permission required.");
            _console.Log("hotkey", "System Settings → Privacy & Security → Accessibility");
            _console.Log("hotkey", "→ enable your terminal app, then restart.");
            return;
        }

        _selfHandle = GCHandle.Alloc(this);
        CGEventTapCallBack callback = EventTapCallback;

        // Tap at session level — catches events regardless of focused app
        _eventTap = CGEventTapCreate(
            kCGSessionEventTap,
            kCGHeadInsertEventTap,
            kCGEventTapOptionListenOnly,  // listen-only: we don't modify events
            (1L << kCGEventKeyDown) | (1L << kCGEventKeyUp) | (1L << kCGEventFlagsChanged),
            callback,
            GCHandle.ToIntPtr(_selfHandle));

        if (_eventTap == IntPtr.Zero)
        {
            _console.LogError("hotkey", "CGEventTapCreate failed — check permissions.");
            _selfHandle.Free();
            return;
        }

        _runLoopSource = CFMachPortCreateRunLoopSource(IntPtr.Zero, _eventTap, 0);
        CFRunLoopAddSource(CFRunLoopGetCurrent(), _runLoopSource, kCFRunLoopCommonModes);
        CGEventTapEnable(_eventTap, true);

        // Blocks until CFRunLoopStop is called in Dispose()
        CFRunLoopRun();

        // Cleanup
        CGEventTapEnable(_eventTap, false);
        CFRunLoopRemoveSource(CFRunLoopGetCurrent(), _runLoopSource, kCFRunLoopCommonModes);
        CFRelease(_runLoopSource);
        CFRelease(_eventTap);
        _selfHandle.Free();
    }

    private static IntPtr EventTapCallback(IntPtr proxy, int type, IntPtr eventRef, IntPtr userInfo)
    {
        var self = (MacOsHotkeyHook)GCHandle.FromIntPtr(userInfo).Target!;

        if (type == kCGEventFlagsChanged)
        {
            // Modifier flags updated; we don't need to track individually because we have mask
            return eventRef;
        }

        if (type == kCGEventKeyDown || type == kCGEventKeyUp)
        {
            long keyCode = CGEventGetIntegerValueField(eventRef, kCGKeyboardEventKeycode);
            long flags = CGEventGetFlags(eventRef);
            
            // Check if this is the hotkey key
            if (keyCode == self._hotkeyKeyCode)
            {
                // Check modifiers match exactly (ignore extra modifiers?)
                bool modifiersMatch = ((ulong)flags & self._modifierFlagsMask) == self._modifierFlagsMask;
                // Optionally allow extra modifiers? For now require exact match.
                if (modifiersMatch)
                {
                    if (type == kCGEventKeyDown && !self._hotkeyKeyDown)
                    {
                        self._hotkeyKeyDown = true;
                        ThreadPool.QueueUserWorkItem(_ => self.HotkeyPressed?.Invoke());
                    }
                    else if (type == kCGEventKeyUp && self._hotkeyKeyDown)
                    {
                        self._hotkeyKeyDown = false;
                        ThreadPool.QueueUserWorkItem(_ => self.HotkeyReleased?.Invoke());
                    }
                }
            }
        }

        return eventRef;
    }

    public void Dispose()
    {
        _cts.Cancel();
        // Signal the run loop on the correct thread
        if (_thread?.IsAlive == true)
            CFRunLoopStop(CFRunLoopGetMain()); // approximate — ideally store ref from thread
    }

    // ── P/Invoke ──────────────────────────────────────────────────────

    private delegate IntPtr CGEventTapCallBack(IntPtr proxy, int type, IntPtr eventRef, IntPtr userInfo);

    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string AppServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    private const int kCGSessionEventTap = 1;
    private const int kCGHeadInsertEventTap = 0;
    private const int kCGEventTapOptionListenOnly = 1;
    private const int kCGKeyboardEventKeycode = 9;
    private static readonly IntPtr kCFRunLoopCommonModes = CFRunLoopCopyCurrentMode(IntPtr.Zero);

    [DllImport(CoreGraphics)] static extern IntPtr CGEventTapCreate(int tap, int place, int options, long eventsOfInterest, CGEventTapCallBack cb, IntPtr userInfo);
    [DllImport(CoreGraphics)] static extern void CGEventTapEnable(IntPtr tap, bool enable);
    [DllImport(CoreGraphics)] static extern long CGEventGetFlags(IntPtr evt);
    [DllImport(CoreGraphics)] static extern long CGEventGetIntegerValueField(IntPtr evt, int field);
    [DllImport(CoreFoundation)] static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr alloc, IntPtr port, int order);
    [DllImport(CoreFoundation)] static extern IntPtr CFRunLoopGetCurrent();
    [DllImport(CoreFoundation)] static extern IntPtr CFRunLoopGetMain();
    [DllImport(CoreFoundation)] static extern void CFRunLoopRun();
    [DllImport(CoreFoundation)] static extern void CFRunLoopStop(IntPtr rl);
    [DllImport(CoreFoundation)] static extern void CFRunLoopAddSource(IntPtr rl, IntPtr source, IntPtr mode);
    [DllImport(CoreFoundation)] static extern void CFRunLoopRemoveSource(IntPtr rl, IntPtr source, IntPtr mode);
    [DllImport(CoreFoundation)] static extern IntPtr CFRunLoopCopyCurrentMode(IntPtr rl);
    [DllImport(CoreFoundation)] static extern void CFRelease(IntPtr cf);
    [DllImport(AppServices)] static extern bool AXIsProcessTrusted();
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using ArayCode.Services;

namespace ArayCode;

/// <summary>
/// Reads raw evdev events from /dev/input/event* to detect global hotkeys.
/// Works in TTY without X11/Wayland. Requires membership in the 'input' group:
///   sudo usermod -aG input $USER
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxEvdevHotkeyHook : IGlobalHotkeyHook
{
    public event Action? HotkeyPressed;
    public event Action? HotkeyReleased;
    public event Action<int>? HotkeyIndexPressed;
    public event Action<int>? HotkeyIndexReleased;
    public event Action? EscapePressed;
    public bool BlockEscape { get; set; }

    private readonly IColorConsole _console;
    private readonly CancellationTokenSource _cts = new();
    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _thread;

    // evdev constants
    private const ushort EV_KEY = 0x01;
    private const int VALUE_DOWN = 1;
    private const int VALUE_REPEAT = 2;
    private const int VALUE_UP = 0;

    // Linux key codes for modifiers (left/right)
    private const ushort KEY_ESC = 1;
    private const ushort KEY_LEFTALT = 56;
    private const ushort KEY_RIGHTALT = 100;
    private const ushort KEY_LEFTCTRL = 29;
    private const ushort KEY_RIGHTCTRL = 97;
    private const ushort KEY_LEFTSHIFT = 42;
    private const ushort KEY_RIGHTSHIFT = 54;
    private const ushort KEY_LEFTMETA = 125;  // Left Super/Windows
    private const ushort KEY_RIGHTMETA = 126;

    // sizeof(struct input_event) on 64-bit Linux:
    //   struct timeval = 8+8, type = 2, code = 2, value = 4  →  24 bytes
    private const int EVENT_SIZE = 24;

    // Hotkey configuration
    private List<Hotkey> _hotkeys = new();
    // Modifier down counts (for left/right variants)
    private int _altDownCount;
    private int _ctrlDownCount;
    private int _shiftDownCount;
    private int _metaDownCount;
    private int _activeHotkeyIndex = -1;

    public void SetHotkey(Hotkey hotkey)
    {
        SetHotkeys(new[] { hotkey });
    }

    public LinuxEvdevHotkeyHook(IColorConsole console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    public void SetHotkeys(IEnumerable<Hotkey> hotkeys)
    {
        _hotkeys = hotkeys.ToList();
        // Reset states
        _altDownCount = 0;
        _ctrlDownCount = 0;
        _shiftDownCount = 0;
        _metaDownCount = 0;
        _activeHotkeyIndex = -1;
    }

    public void Start()
    {
        _thread = new Thread(ReadLoop) { IsBackground = true, Name = "EvdevHotkeyLoop" };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5)); // give devices time to enumerate
    }

    // ── main device-reading loop ──────────────────────────────────────

    private void ReadLoop()
    {
        var ct = _cts.Token;
        var readTasks = new List<Task>();

        try
        {
            // Discover devices, starting a reader immediately after each is identified
            // so a keypress during discovery of later devices is not missed.
            foreach (var path in Directory.GetFiles("/dev/input", "event*").OrderBy(x => x))
            {
                FileStream? fs = TryOpenDevice(path);
                if (fs == null) continue;

                // Check if this device reports key events (without re-opening)
                if (!IsKeyboardDevice(fs))
                {
                    fs.Dispose();
                    continue;
                }

                // Start reading from this device immediately.
                readTasks.Add(ReadDeviceWithStreamAsync(fs, path, ct));

                // Signal readiness as soon as the first device starts reading.
                if (!_ready.IsSet)
                    _ready.Set();
            }
        }
        catch (DirectoryNotFoundException ex)
        {
            _console.Log("hotkey", $"Keyboard device directory not found: {ex.Message}");
            _ready.Set();
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            _console.Log("hotkey", $"No permission to access keyboard devices: {ex.Message}");
            _console.Log("hotkey", "Fix: sudo usermod -aG input $USER  (then re-login)");
            _ready.Set();
            return;
        }

        if (readTasks.Count == 0)
        {
            _console.Log("hotkey", "No accessible keyboard devices found in /dev/input/.");
            _console.Log("hotkey", "Fix: sudo usermod -aG input $USER  (then re-login)");
            _ready.Set();
            return;
        }

        _console.Log("hotkey", $"Watching {readTasks.Count} keyboard device(s) for hotkey");
        _ready.Set(); // ensure unblock (already set above, but idempotent)

        try
        {
            Task.WaitAll(readTasks.ToArray(), ct);
        }
        catch (OperationCanceledException) { }
        catch (AggregateException) { }
    }

    /// <summary>Checks whether an already-open device file reports key events.</summary>
    private static bool IsKeyboardDevice(FileStream fs)
    {
        try
        {
            int fd = (int)fs.SafeFileHandle.DangerousGetHandle();
            var evBits = new byte[4];
            if (ioctl(fd, EVIOCGBIT(0, evBits.Length), evBits) < 0) return false;
            return (evBits[EV_KEY / 8] & (1 << (EV_KEY % 8))) != 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task ReadDeviceWithStreamAsync(FileStream fs, string path, CancellationToken ct)
    {
        await using (fs)
        {
            var buf = new byte[EVENT_SIZE];
            while (!ct.IsCancellationRequested)
            {
                int totalRead = 0;
                try
                {
                    while (totalRead < EVENT_SIZE)
                    {
                        int n = await fs.ReadAsync(buf.AsMemory(totalRead, EVENT_SIZE - totalRead), ct);
                        if (n == 0) return;
                        totalRead += n;
                    }
                }
                catch (OperationCanceledException) { return; }
                catch { return; }

                ProcessEvent(buf);
            }
        }
    }

    private void ProcessEvent(byte[] data)
    {
        ushort type = BitConverter.ToUInt16(data, 16);
        ushort code = BitConverter.ToUInt16(data, 18);
        int value = BitConverter.ToInt32(data, 20);

        if (type != EV_KEY) return;

        // Update modifier counts.
        // Explicitly skip VALUE_REPEAT — auto-repeat would keep incrementing the
        // count and break ModifiersMatch for any subsequent key press.
        switch (code)
        {
            case KEY_LEFTALT:
            case KEY_RIGHTALT:
                if (value == VALUE_DOWN) Interlocked.Increment(ref _altDownCount);
                else if (value == VALUE_UP) Interlocked.Decrement(ref _altDownCount);
                break;
            case KEY_LEFTCTRL:
            case KEY_RIGHTCTRL:
                if (value == VALUE_DOWN) Interlocked.Increment(ref _ctrlDownCount);
                else if (value == VALUE_UP) Interlocked.Decrement(ref _ctrlDownCount);
                break;
            case KEY_LEFTSHIFT:
            case KEY_RIGHTSHIFT:
                if (value == VALUE_DOWN) Interlocked.Increment(ref _shiftDownCount);
                else if (value == VALUE_UP) Interlocked.Decrement(ref _shiftDownCount);
                break;
            case KEY_LEFTMETA:
            case KEY_RIGHTMETA:
                if (value == VALUE_DOWN) Interlocked.Increment(ref _metaDownCount);
                else if (value == VALUE_UP) Interlocked.Decrement(ref _metaDownCount);
                break;
        }

        // Check for Escape key (cancel recording)
        if (code == KEY_ESC && value == VALUE_DOWN)
        {
            if (BlockEscape)
            {
                ThreadPool.QueueUserWorkItem(_ => EscapePressed?.Invoke());
                return; // don't fall through into hotkey matching
            }
        }

        // Check all configured hotkeys.
        // Use Interlocked.CompareExchange to guard against concurrent events from
        // multiple device tasks both seeing _activeHotkeyIndex == -1 simultaneously.
        int matchedIndex = FindMatchingHotkeyIndex(code);
        if (value == VALUE_DOWN && matchedIndex >= 0 &&
            Interlocked.CompareExchange(ref _activeHotkeyIndex, matchedIndex, -1) == -1)
        {
            int capturedIndex = matchedIndex;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                HotkeyPressed?.Invoke();
                HotkeyIndexPressed?.Invoke(capturedIndex);
            });
        }
        else if (value == VALUE_UP && _activeHotkeyIndex >= 0)
        {
            // Fire release when the active hotkey's key goes up.
            // Use the saved index rather than re-matching — modifiers may
            // already be released (user lifted Alt before D), causing
            // FindMatchingHotkeyIndex to miss and _activeHotkeyIndex to stick.
            int active = Volatile.Read(ref _activeHotkeyIndex);
            if (active >= 0 && active < _hotkeys.Count &&
                HotkeyMapping.GetPlatformKeyCode(_hotkeys[active].Key) == code)
            {
                int capturedIndex = Interlocked.Exchange(ref _activeHotkeyIndex, -1);
                if (capturedIndex >= 0)
                {
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        HotkeyReleased?.Invoke();
                        HotkeyIndexReleased?.Invoke(capturedIndex);
                    });
                }
            }
        }
    }

    private int FindMatchingHotkeyIndex(ushort keyCode)
    {
        for (int i = 0; i < _hotkeys.Count; i++)
        {
            var hk = _hotkeys[i];
            if (HotkeyMapping.GetPlatformKeyCode(hk.Key) == keyCode && ModifiersMatch(hk.Modifiers))
                return i;
        }
        return -1;
    }

    private bool ModifiersMatch(HashSet<Modifier> modifiers)
    {
        bool altRequired = modifiers.Contains(Modifier.Alt);
        bool ctrlRequired = modifiers.Contains(Modifier.Ctrl);
        bool shiftRequired = modifiers.Contains(Modifier.Shift);
        bool winRequired = modifiers.Contains(Modifier.Win);

        bool altPressed = Volatile.Read(ref _altDownCount) > 0;
        bool ctrlPressed = Volatile.Read(ref _ctrlDownCount) > 0;
        bool shiftPressed = Volatile.Read(ref _shiftDownCount) > 0;
        bool winPressed = Volatile.Read(ref _metaDownCount) > 0;

        return altRequired == altPressed &&
               ctrlRequired == ctrlPressed &&
               shiftRequired == shiftPressed &&
               winRequired == winPressed;
    }

    // EVIOCGBIT(type, len) ioctl number for Linux/x86-64.
    // Direction 2 = _IOR (read from device into userspace).
    // Was incorrectly 0x80000000u (write direction bit), making every ioctl
    // return -1 and IsKeyboardDevice always return false.
    private static int EVIOCGBIT(int type, int len) =>
        (int)((2u << 30) | ((uint)len << 16) | ((uint)'E' << 8) | (uint)(0x20 + type));

    private static FileStream? TryOpenDevice(string path)
    {
        try
        {
            return new FileStream(path, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite,
                bufferSize: 0, useAsync: true);
        }
        catch { return null; }
    }

    public void Dispose() => _cts.Cancel();

    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    private static extern int ioctl(int fd, int request, byte[] arg);
}
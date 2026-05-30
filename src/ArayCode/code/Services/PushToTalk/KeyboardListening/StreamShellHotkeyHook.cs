using ArayCode.Services;

namespace ArayCode;

/// <summary>
/// IGlobalHotkeyHook implementation that uses StreamShell's terminal-level
/// key subscriptions (<see cref="IStreamShellHost.SubscribeKey"/>) instead
/// of OS-global hotkeys.  Hotkeys only work while the terminal is focused,
/// and key-up events are not detected (hold-to-talk is not supported).
/// </summary>
#pragma warning disable CS0067 // events are intentionally never fired (no key-up in terminal)
internal sealed class StreamShellHotkeyHook : IGlobalHotkeyHook
{
    public event Action? HotkeyPressed;
    public event Action? HotkeyReleased;
    public event Action<int>? HotkeyIndexPressed;
    public event Action<int>? HotkeyIndexReleased;
    public event Action? EscapePressed;

    private readonly IStreamShellHost _shellHost;
    private readonly IColorConsole _console;
    private readonly object _lock = new();
    private readonly List<IDisposable> _subscriptions = new();

    private volatile List<Hotkey> _hotkeys = new();
    private volatile bool _blockEscape;
    private bool _started;

    public StreamShellHotkeyHook(IStreamShellHost shellHost, IColorConsole console)
    {
        _shellHost = shellHost ?? throw new ArgumentNullException(nameof(shellHost));
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    public bool BlockEscape
    {
        get => _blockEscape;
        set => _blockEscape = value;
    }

    public void SetHotkey(Hotkey hotkey) => SetHotkeys(new[] { hotkey });

    public void SetHotkeys(IEnumerable<Hotkey> hotkeys)
    {
        lock (_lock)
        {
            _hotkeys = hotkeys.ToList();

            if (_started)
                Resubscribe();
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_started) return;
            _started = true;
            Resubscribe();
        }
    }

    /// <summary>Unsubscribes old subscriptions and creates new ones.</summary>
    private void Resubscribe()
    {
        foreach (var s in _subscriptions)
            s?.Dispose();
        _subscriptions.Clear();

        // Single predicate subscription that intercepts escape and all hotkeys.
        var sub = _shellHost.SubscribeKey(Predicate, Handler);
        if (sub != null)
            _subscriptions.Add(sub);
    }

    /// <summary>Predicate: return true to consume the key (prevent it reaching normal input).</summary>
    private bool Predicate(ConsoleKeyInfo key)
    {
        // Always consume Escape when blocked
        if (key.Key == ConsoleKey.Escape && _blockEscape)
            return true;

        return FindMatchingHotkeyIndex(key) >= 0;
    }

    /// <summary>Handler for consumed keys.</summary>
    private void Handler(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)
        {
            EscapePressed?.Invoke();
            return;
        }

        int index = FindMatchingHotkeyIndex(key);
        if (index >= 0)
        {
            // Terminal processes keys serially — no concurrent access concern.
            // No key-up events in terminal mode, so each key-down must be
            // processed independently (the evdev/X11 hooks use _activeHotkeyIndex
            // to gate press-release cycles, which doesn't apply here).
            ThreadPool.QueueUserWorkItem(_ =>
            {
                HotkeyPressed?.Invoke();
                HotkeyIndexPressed?.Invoke(index);
            });
        }
    }

    private int FindMatchingHotkeyIndex(ConsoleKeyInfo key)
    {
        var keyMods = key.Modifiers;
        for (int i = 0; i < _hotkeys.Count; i++)
        {
            var hk = _hotkeys[i];
            if (KeyMatches(key.Key, hk.Key) && ModifiersMatch(keyMods, hk.Modifiers))
                return i;
        }
        return -1;
    }

    /// <summary>Maps a ConsoleKey to our internal Key model and checks for match.</summary>
    private static bool KeyMatches(ConsoleKey consoleKey, Key key)
    {
        var mapped = ToConsoleKey(key);
        if (mapped.HasValue && consoleKey == mapped.Value)
            return true;

        // Fallback: for keys where the ConsoleKey mapping is ambiguous
        // (e.g. '=' mapped to (ConsoleKey)61 by LinuxTerminal), also
        // check by the ASCII value of the ConsoleKey.
        if (key.Special == SpecialKey.None)
        {
            char upper = char.ToUpperInvariant(key.Value);
            // ConsoleKey A-Z = 65-90, 0-9 = 48-57
            int cv = (int)consoleKey;
            if (cv >= 48 && cv <= 57 && upper >= '0' && upper <= '9')
                return (cv - 48) == (upper - '0');
            if (cv >= 65 && cv <= 90 && upper >= 'A' && upper <= 'Z')
                return cv == upper;
        }

        // Also try comparing ConsoleKey's numeric value as ASCII.
        // LinuxTerminal maps single bytes to (ConsoleKey)byteValue,
        // so pressing '=' (0x3D) gives ConsoleKey 61 which == '='.
        if (key.Special == SpecialKey.None)
            return (int)consoleKey == key.Value;

        return false;
    }

    private static bool ModifiersMatch(ConsoleModifiers actual, HashSet<Modifier> expected)
    {
        // Check that all expected modifiers are pressed.
        // We do NOT require an exact match — extra modifiers (like Shift
        // needed to type '=' on a US keyboard) are tolerated.
        foreach (var mod in expected)
        {
            if (!HasModifier(actual, mod))
                return false;
        }
        return true;
    }

    private static bool HasModifier(ConsoleModifiers mods, Modifier mod)
    {
        return mod switch
        {
            Modifier.Alt => mods.HasFlag(ConsoleModifiers.Alt),
            Modifier.Ctrl => mods.HasFlag(ConsoleModifiers.Control),
            Modifier.Shift => mods.HasFlag(ConsoleModifiers.Shift),
            Modifier.Win => false, // ConsoleModifiers has no Win/Meta equivalent
            _ => false,
        };
    }

    private static ConsoleKey? ToConsoleKey(Key key)
    {
        if (key.Special != SpecialKey.None)
        {
            return key.Special switch
            {
                SpecialKey.Space => ConsoleKey.Spacebar,
                SpecialKey.Equal => ConsoleKey.OemPlus, // Windows; LinuxTerminal uses (ConsoleKey)61
                SpecialKey.Minus => ConsoleKey.OemMinus,
                SpecialKey.F1 => ConsoleKey.F1,
                SpecialKey.F2 => ConsoleKey.F2,
                SpecialKey.F3 => ConsoleKey.F3,
                SpecialKey.F4 => ConsoleKey.F4,
                SpecialKey.F5 => ConsoleKey.F5,
                SpecialKey.F6 => ConsoleKey.F6,
                SpecialKey.F7 => ConsoleKey.F7,
                SpecialKey.F8 => ConsoleKey.F8,
                SpecialKey.F9 => ConsoleKey.F9,
                SpecialKey.F10 => ConsoleKey.F10,
                SpecialKey.F11 => ConsoleKey.F11,
                SpecialKey.F12 => ConsoleKey.F12,
                _ => null,
            };
        }

        if (key.Value >= 'A' && key.Value <= 'Z')
            return ConsoleKey.A + (key.Value - 'A');

        if (key.Value >= '0' && key.Value <= '9')
            return ConsoleKey.D0 + (key.Value - '0');

        return null;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var sub in _subscriptions)
                sub?.Dispose();
            _subscriptions.Clear();
        }
    }
}

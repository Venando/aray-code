using System;
using System.Collections.Generic;

namespace ArayCode;

/// <summary>
/// Linux evdev key code lookups for hotkeys (from input-event-codes.h).
/// Extracted from HotkeyMapping for Single Responsibility.
/// </summary>
public static class LinuxKeyCodeProvider
{
    private static readonly Dictionary<Key, int> KeyCodes = new()
    {
        [new Key('A')] = 30, [new Key('B')] = 48,
        [new Key('C')] = 46, [new Key('D')] = 32,
        [new Key('E')] = 18, [new Key('F')] = 33,
        [new Key('G')] = 34, [new Key('H')] = 35,
        [new Key('I')] = 23, [new Key('J')] = 36,
        [new Key('K')] = 37, [new Key('L')] = 38,
        [new Key('M')] = 50, [new Key('N')] = 49,
        [new Key('O')] = 24, [new Key('P')] = 25,
        [new Key('Q')] = 16, [new Key('R')] = 19,
        [new Key('S')] = 31, [new Key('T')] = 20,
        [new Key('U')] = 22, [new Key('V')] = 47,
        [new Key('W')] = 17, [new Key('X')] = 45,
        [new Key('Y')] = 21, [new Key('Z')] = 44,
        [new Key('0')] = 11, [new Key('1')] = 2,
        [new Key('2')] = 3,  [new Key('3')] = 4,
        [new Key('4')] = 5,  [new Key('5')] = 6,
        [new Key('6')] = 7,  [new Key('7')] = 8,
        [new Key('8')] = 9,  [new Key('9')] = 10,
        [Key.Space] = 57,  // KEY_SPACE
        [Key.Equal] = 13,  // KEY_EQUAL
        [Key.Minus] = 12,  // KEY_MINUS
        [Key.F1] = 59,  [Key.F2] = 60,
        [Key.F3] = 61,  [Key.F4] = 62,
        [Key.F5] = 63,  [Key.F6] = 64,
        [Key.F7] = 65,  [Key.F8] = 66,
        [Key.F9] = 67,  [Key.F10] = 68,
        [Key.F11] = 87, [Key.F12] = 88,
    };

    public static int GetKeyCode(Key key)
        => KeyCodes.TryGetValue(key, out var code) ? code
        : throw new NotSupportedException($"Key {key} not mapped for Linux");

    /// <summary>
    /// Linux evdev doesn't use modifier flags in the same way as Windows.
    /// Modifier keys are tracked individually via key codes.
    /// </summary>
    public static ulong GetModifierFlag(Modifier mod) => 0;
}

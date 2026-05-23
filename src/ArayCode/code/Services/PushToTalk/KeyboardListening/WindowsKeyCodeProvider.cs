using System;
using System.Collections.Generic;

namespace ArayCode;

/// <summary>
/// Windows-specific virtual key code lookups for hotkeys.
/// Extracted from HotkeyMapping for Single Responsibility.
/// Uses static dictionaries for O(1) lookups instead of large switch statements.
/// </summary>
public static class WindowsKeyCodeProvider
{
    private static readonly Dictionary<Key, int> KeyCodes = new()
    {
        [new Key('A')] = 0x41, [new Key('B')] = 0x42,
        [new Key('C')] = 0x43, [new Key('D')] = 0x44,
        [new Key('E')] = 0x45, [new Key('F')] = 0x46,
        [new Key('G')] = 0x47, [new Key('H')] = 0x48,
        [new Key('I')] = 0x49, [new Key('J')] = 0x4A,
        [new Key('K')] = 0x4B, [new Key('L')] = 0x4C,
        [new Key('M')] = 0x4D, [new Key('N')] = 0x4E,
        [new Key('O')] = 0x4F, [new Key('P')] = 0x50,
        [new Key('Q')] = 0x51, [new Key('R')] = 0x52,
        [new Key('S')] = 0x53, [new Key('T')] = 0x54,
        [new Key('U')] = 0x55, [new Key('V')] = 0x56,
        [new Key('W')] = 0x57, [new Key('X')] = 0x58,
        [new Key('Y')] = 0x59, [new Key('Z')] = 0x5A,
        [new Key('0')] = 0x30, [new Key('1')] = 0x31,
        [new Key('2')] = 0x32, [new Key('3')] = 0x33,
        [new Key('4')] = 0x34, [new Key('5')] = 0x35,
        [new Key('6')] = 0x36, [new Key('7')] = 0x37,
        [new Key('8')] = 0x38, [new Key('9')] = 0x39,
        [Key.Space] = 0x20,  // VK_SPACE
        [Key.Equal] = 0xBB,  // VK_OEM_PLUS
        [Key.Minus] = 0xBD,  // VK_OEM_MINUS
        [Key.F1] = 0x70,  [Key.F2] = 0x71,
        [Key.F3] = 0x72,  [Key.F4] = 0x73,
        [Key.F5] = 0x74,  [Key.F6] = 0x75,
        [Key.F7] = 0x76,  [Key.F8] = 0x77,
        [Key.F9] = 0x78,  [Key.F10] = 0x79,
        [Key.F11] = 0x7A, [Key.F12] = 0x7B,
    };

    private static readonly Dictionary<Modifier, ulong> ModifierFlags = new()
    {
        [Modifier.Alt] = 0x0001,
        [Modifier.Ctrl] = 0x0002,
        [Modifier.Shift] = 0x0004,
        [Modifier.Win] = 0x0008,
    };

    public static int GetKeyCode(Key key)
        => KeyCodes.TryGetValue(key, out var code) ? code
        : throw new NotSupportedException($"Key {key} not mapped for Windows");

    public static ulong GetModifierFlag(Modifier mod)
        => ModifierFlags.TryGetValue(mod, out var flag) ? flag : 0;
}

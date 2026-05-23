using System;
using System.Collections.Generic;

namespace ArayCode;

/// <summary>
/// macOS virtual key code lookups for hotkeys (from Events.h).
/// Extracted from HotkeyMapping for Single Responsibility.
/// </summary>
public static class MacKeyCodeProvider
{
    private static readonly Dictionary<Key, int> KeyCodes = new()
    {
        [new Key('A')] = 0x00, [new Key('B')] = 0x0B,
        [new Key('C')] = 0x08, [new Key('D')] = 0x02,
        [new Key('E')] = 0x0E, [new Key('F')] = 0x03,
        [new Key('G')] = 0x05, [new Key('H')] = 0x04,
        [new Key('I')] = 0x22, [new Key('J')] = 0x26,
        [new Key('K')] = 0x28, [new Key('L')] = 0x25,
        [new Key('M')] = 0x2E, [new Key('N')] = 0x2D,
        [new Key('O')] = 0x1F, [new Key('P')] = 0x23,
        [new Key('Q')] = 0x0C, [new Key('R')] = 0x0F,
        [new Key('S')] = 0x01, [new Key('T')] = 0x11,
        [new Key('U')] = 0x20, [new Key('V')] = 0x09,
        [new Key('W')] = 0x0D, [new Key('X')] = 0x07,
        [new Key('Y')] = 0x10, [new Key('Z')] = 0x06,
        [new Key('0')] = 0x1D, [new Key('1')] = 0x12,
        [new Key('2')] = 0x13, [new Key('3')] = 0x14,
        [new Key('4')] = 0x15, [new Key('5')] = 0x17,
        [new Key('6')] = 0x16, [new Key('7')] = 0x1A,
        [new Key('8')] = 0x1C, [new Key('9')] = 0x19,
        [Key.Space] = 0x31, // kVK_Space
        [Key.Equal] = 0x18, // kVK_Equal
        [Key.Minus] = 0x1B, // kVK_Minus
        [Key.F1] = 0x7A, [Key.F2] = 0x78,
        [Key.F3] = 0x63, [Key.F4] = 0x76,
        [Key.F5] = 0x60, [Key.F6] = 0x61,
        [Key.F7] = 0x62, [Key.F8] = 0x64,
        [Key.F9] = 0x65, [Key.F10] = 0x6D,
        [Key.F11] = 0x67, [Key.F12] = 0x6F,
    };

    private static readonly Dictionary<Modifier, ulong> ModifierFlags = new()
    {
        [Modifier.Alt] = 0x00080000,   // kCGEventFlagMaskAlternate
        [Modifier.Ctrl] = 0x00040000,  // kCGEventFlagMaskControl
        [Modifier.Shift] = 0x00020000, // kCGEventFlagMaskShift
        [Modifier.Win] = 0x00100000,   // kCGEventFlagMaskCommand
    };

    public static int GetKeyCode(Key key)
        => KeyCodes.TryGetValue(key, out var code) ? code
        : throw new NotSupportedException($"Key {key} not mapped for macOS");

    public static ulong GetModifierFlag(Modifier mod)
        => ModifierFlags.TryGetValue(mod, out var flag) ? flag : 0;
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace ArayCode;

/// <summary>
/// Pure string parsing logic for hotkey combination strings.
/// Extracted from HotkeyMapping for Single Responsibility.
/// Stateless — all member methods are static.
/// </summary>
public static class HotkeyParser
{
    /// <summary>
    /// Parses a hotkey combination string like "Alt+Shift+Space" into a <see cref="Hotkey"/>.
    /// </summary>
    public static Hotkey Parse(string combination)
    {
        if (string.IsNullOrWhiteSpace(combination))
            throw new ArgumentException("Hotkey combination cannot be empty", nameof(combination));

        var parts = combination.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            throw new ArgumentException("Invalid hotkey format", nameof(combination));

        var keyPart = parts[^1];
        var modifierParts = parts[..^1];

        var modifiers = ParseModifiers(modifierParts);
        var key = ParseKey(keyPart);

        return new Hotkey(key, modifiers);
    }

    public static HashSet<Modifier> ParseModifiers(string[] modifierParts)
    {
        var set = new HashSet<Modifier>();
        foreach (var part in modifierParts)
        {
            var mod = part.ToUpperInvariant() switch
            {
                "ALT" => Modifier.Alt,
                "CTRL" or "CONTROL" => Modifier.Ctrl,
                "SHIFT" => Modifier.Shift,
                "WIN" or "META" or "SUPER" => Modifier.Win,
                _ => throw new ArgumentException($"Unknown modifier: {part}")
            };
            set.Add(mod);
        }
        return set;
    }

    public static Key ParseKey(string keyPart)
    {
        var normalized = keyPart.ToUpperInvariant();
        return normalized switch
        {
            "SPACE" => Key.Space,
            "EQUALS" or "=" => Key.Equal,
            "PLUS" or "+" => Key.Equal,
            "MINUS" or "-" => Key.Minus,
            "F1" => Key.F1,
            "F2" => Key.F2,
            "F3" => Key.F3,
            "F4" => Key.F4,
            "F5" => Key.F5,
            "F6" => Key.F6,
            "F7" => Key.F7,
            "F8" => Key.F8,
            "F9" => Key.F9,
            "F10" => Key.F10,
            "F11" => Key.F11,
            "F12" => Key.F12,
            _ when normalized.Length == 1 && char.IsLetter(normalized[0]) => new Key(normalized[0]),
            _ when normalized.Length == 1 && char.IsDigit(normalized[0]) => new Key(normalized[0]),
            _ => throw new ArgumentException($"Unsupported key: {keyPart}")
        };
    }
}

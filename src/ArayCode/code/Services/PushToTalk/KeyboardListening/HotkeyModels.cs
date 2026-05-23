using System;
using System.Collections.Generic;

namespace ArayCode;

public readonly struct Key : IEquatable<Key>
{
    public readonly char Value;
    public readonly SpecialKey Special;

    public Key(char c)
    {
        Value = char.ToUpperInvariant(c);
        Special = SpecialKey.None;
    }

    private Key(SpecialKey special)
    {
        Value = '\0';
        Special = special;
    }

    public static Key Space => new(SpecialKey.Space);
    public static Key Equal => new(SpecialKey.Equal);
    public static Key Minus => new(SpecialKey.Minus);
    public static Key F1 => new(SpecialKey.F1);
    public static Key F2 => new(SpecialKey.F2);
    public static Key F3 => new(SpecialKey.F3);
    public static Key F4 => new(SpecialKey.F4);
    public static Key F5 => new(SpecialKey.F5);
    public static Key F6 => new(SpecialKey.F6);
    public static Key F7 => new(SpecialKey.F7);
    public static Key F8 => new(SpecialKey.F8);
    public static Key F9 => new(SpecialKey.F9);
    public static Key F10 => new(SpecialKey.F10);
    public static Key F11 => new(SpecialKey.F11);
    public static Key F12 => new(SpecialKey.F12);

    public override string ToString()
    {
        if (Special != SpecialKey.None)
            return Special.ToString();
        return Value.ToString();
    }

    public bool Equals(Key other) => Value == other.Value && Special == other.Special;
    public override bool Equals(object? obj) => obj is Key other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Value, Special);
    public static bool operator ==(Key left, Key right) => left.Equals(right);
    public static bool operator !=(Key left, Key right) => !left.Equals(right);
}

public enum SpecialKey
{
    None,
    Space,
    Equal,
    Minus,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12
}

public enum Modifier
{
    Alt,
    Ctrl,
    Shift,
    Win
}

public record Hotkey(Key Key, HashSet<Modifier> Modifiers);

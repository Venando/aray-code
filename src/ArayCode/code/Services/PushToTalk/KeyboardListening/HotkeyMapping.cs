using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ArayCode;

/// <summary>
/// Platform-specific hotkey key code resolution.
/// Types used by this class (<see cref="Key"/>, <see cref="SpecialKey"/>, <see cref="Modifier"/>, <see cref="Hotkey"/>) 
/// are defined in <see cref="HotkeyModels"/>.
/// Parsing logic has been moved to <see cref="HotkeyParser"/>.
/// Platform-specific key code providers can be found in:
/// <see cref="WindowsKeyCodeProvider"/>, <see cref="LinuxKeyCodeProvider"/>, <see cref="MacKeyCodeProvider"/>.
/// </summary>
public static class HotkeyMapping
{
    /// <summary>
    /// Parses a hotkey combination string.
    /// Delegates to <see cref="HotkeyParser"/>.
    /// </summary>
    public static Hotkey Parse(string combination) => HotkeyParser.Parse(combination);

    /// <summary>
    /// Returns the platform-specific virtual key code for the given key.
    /// Selects the appropriate provider at runtime per call.
    /// </summary>
    public static int GetPlatformKeyCode(Key key)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return WindowsKeyCodeProvider.GetKeyCode(key);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return LinuxKeyCodeProvider.GetKeyCode(key);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return MacKeyCodeProvider.GetKeyCode(key);

        throw new PlatformNotSupportedException();
    }

    /// <summary>
    /// Returns a mask representing the modifier flags for the given modifiers on the current platform.
    /// </summary>
    public static ulong GetPlatformModifierFlags(HashSet<Modifier> modifiers)
    {
        ulong mask = 0;
        foreach (var mod in modifiers)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                mask |= WindowsKeyCodeProvider.GetModifierFlag(mod);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                mask |= LinuxKeyCodeProvider.GetModifierFlag(mod);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                mask |= MacKeyCodeProvider.GetModifierFlag(mod);
        }
        return mask;
    }
}

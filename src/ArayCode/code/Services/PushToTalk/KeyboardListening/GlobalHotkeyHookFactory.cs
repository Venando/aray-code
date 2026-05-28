using System.Runtime.InteropServices;
using ArayCode.Services;

namespace ArayCode;

internal static class GlobalHotkeyHookFactory
{
    public static IGlobalHotkeyHook Create(IColorConsole console)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new WindowsHotkeyHook();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Prefer evdev (/dev/input/event*) — works when running as root
            // or when the user is in the 'input' group.
            if (CanUseEvdev())
                return new LinuxEvdevHotkeyHook(console);

            // Fall back to X11 when evdev is inaccessible.
            if (CanUseX11())
                return new LinuxX11HotkeyHook(console);

            console.PrintWarning("Hotkey. No keyboard hook available (neither evdev nor X11). Hotkeys disabled.");
            
            return new NoopHotkeyHook();
        }

        throw new PlatformNotSupportedException(
            $"Global hotkeys are not supported on {RuntimeInformation.OSDescription}");
    }

    private static bool CanUseEvdev()
    {
        try
        {
            var files = System.IO.Directory.GetFiles("/dev/input", "event*");
            if (files.Length == 0) return false;

            using var fs = new System.IO.FileStream(files[0],
                System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool CanUseX11()
    {
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));
    }
}
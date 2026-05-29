using System.Runtime.InteropServices;
using ArayCode.Services;

namespace ArayCode;

internal static class GlobalHotkeyHookFactory
{
    /// <summary>
    /// Creates the appropriate hotkey hook based on platform and config.
    /// When <paramref name="config"/> is null or <c>IsGlobalHotkeys</c> is true,
    /// tries OS-level global hooks first. Falls back to terminal subscriptions
    /// via StreamShell when the global path is unavailable.
    /// </summary>
    public static IGlobalHotkeyHook Create(
        IColorConsole console,
        AppConfig? config = null,
        IStreamShellHost? shellHost = null)
    {
        bool useGlobal = config?.IsGlobalHotkeys ?? true;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (useGlobal)
                return new WindowsHotkeyHook();

            return CreateStreamShellHook(shellHost, console);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (useGlobal)
            {
                if (CanUseEvdev())
                    return new LinuxEvdevHotkeyHook(console);

                console.PrintWarning(
                    "Global hotkeys enabled but evdev (/dev/input/event*) not accessible. " +
                    "Falling back to terminal-scoped hotkeys (only work while terminal is focused).\n" +
                    "  To use global hotkeys: sudo usermod -aG input $USER  (then re-login)\n" +
                    "  To suppress this warning: set IsGlobalHotkeys=false in config");
            }

            return CreateStreamShellHook(shellHost, console);
        }

        throw new PlatformNotSupportedException(
            $"Global hotkeys are not supported on {RuntimeInformation.OSDescription}");
    }

    private static IGlobalHotkeyHook CreateStreamShellHook(IStreamShellHost? shellHost, IColorConsole console)
    {
        if (shellHost != null)
            return new StreamShellHotkeyHook(shellHost, console);

        console.PrintWarning("StreamShell host not available for terminal hotkeys. Hotkeys disabled.");
        return new NoopHotkeyHook();
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
}

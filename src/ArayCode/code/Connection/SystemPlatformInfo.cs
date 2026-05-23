namespace ArayCode;

/// <summary>
/// Default implementation that delegates to OperatingSystem checks.
/// </summary>
public sealed class SystemPlatformInfo : IPlatformInfo
{
    public string GetPlatform()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsMacOS()) return "macos";
        return "linux";
    }
}

namespace ArayCode;

/// <summary>
/// Abstraction for platform detection, enabling testability.
/// </summary>
public interface IPlatformInfo
{
    string GetPlatform(); // returns "windows", "macos", or "linux"
}

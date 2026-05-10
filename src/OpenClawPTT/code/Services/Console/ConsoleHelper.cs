namespace OpenClawPTT.Services;

/// <summary>
/// Shared utilities for console-level operations.
/// </summary>
public static class ConsoleHelper
{
    /// <summary>
    /// Safely gets the console window width, falling back to 80 when the
    /// console handle is unavailable (headless/test environments).
    /// </summary>
    public static int GetWindowWidth()
    {
        try
        {
            int w = Console.WindowWidth;
            return w > 0 ? w : 80;
        }
        catch
        {
            return 80;
        }
    }
}

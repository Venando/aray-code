namespace OpenClawPTT.Services;

/// <summary>
/// Provides safe access to console metrics (window width, etc.)
/// with sensible fallbacks for headless/redirected environments.
/// </summary>
public static class ConsoleMetrics
{
    /// <summary>
    /// Gets the console window width, returning <paramref name="fallback"/>
    /// if the console is unavailable or in a redirected environment.
    /// </summary>
    public static int GetWindowWidth(int fallback = 80)
    {
        try
        {
            int w = Console.WindowWidth;
            return w > 0 ? w : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>
    /// Gets the console window height, returning <paramref name="fallback"/>
    /// if the console is unavailable or in a redirected environment.
    /// </summary>
    internal static int GetWindowHeight(int fallback = 30)
    {
        try
        {
            int h = Console.WindowHeight;
            return h > 0 ? h : fallback;
        }
        catch
        {
            return fallback;
        }
    }
}

using System.Globalization;
using Wcwidth;

namespace OpenClawPTT;

/// <summary>
/// Utility class for measuring the display width of text, using
/// <see cref="UnicodeCalculator.GetWidth(char)"/> from the Wcwidth
/// library for accurate East Asian width and emoji handling.
/// </summary>
public static class CharacterWidth
{
    /// <summary>
    /// Returns the display width of a single character according to
    /// Unicode East Asian Width (EA) and emoji properties.
    /// Most terminals follow these rules.
    /// </summary>
    public static int GetDisplayWidth(char c)
    {
        int w = UnicodeCalculator.GetWidth(c);
        // Wcwidth returns 0 for control chars (\n, \t, etc.) and -1 for nonprintable.
        // For display width purposes treat them as 1 so they don't undercount.
        if (w <= 0) return 1;
        return w;
    }

    /// <summary>
    /// Returns the total display width of a string, accounting for
    /// East Asian full-width characters and emoji.
    /// </summary>
    public static int GetDisplayWidth(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        int width = 0;
        foreach (char c in text)
        {
            width += GetDisplayWidth(c);
        }
        return width;
    }
}

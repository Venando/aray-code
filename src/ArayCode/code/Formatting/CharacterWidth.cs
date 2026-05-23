using System.Globalization;
using Wcwidth;

namespace ArayCode;

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

    /// <summary>
    /// Splits text into lines each not exceeding <paramref name="maxWidth"/>
    /// visual columns. Breaks at word boundaries (whitespace) when possible,
    /// or at the exact column limit otherwise.
    /// Replaces the legacy <c>TextWidth.WrapToVisualWidth</c>.
    /// </summary>
    public static List<string> WrapToWidth(string text, int maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text) || maxWidth <= 0)
        {
            if (!string.IsNullOrEmpty(text))
                lines.Add(text);
            return lines;
        }

        // Treat tab as a single space for wrapping purposes
        text = text.Replace('\t', ' ');

        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == '\n')
            {
                lines.Add("");
                i++;
                continue;
            }

            int lineStart = i;
            int visualWidth = 0;

            // Find the longest substring that fits within maxWidth
            while (i < text.Length && text[i] != '\n')
            {
                int cw = GetDisplayWidth(text[i]);
                if (visualWidth + cw > maxWidth)
                    break;
                visualWidth += cw;
                i++;
            }

            int lineEnd = i;

            // If we can't fit even one character, force-break at current position
            if (lineEnd == lineStart && i < text.Length)
            {
                int cw = GetDisplayWidth(text[i]);
                lineEnd = i + 1;
                i = lineEnd;
                lines.Add(text[lineStart..lineEnd]);
                continue;
            }

            // If we broke mid-word, try to find the last whitespace for a cleaner break
            if (i < text.Length && text[i] != '\n' && lineEnd > lineStart)
            {
                int breakAt = -1;
                // Scan backwards from the break point for any whitespace
                for (int j = lineEnd - 1; j >= lineStart; j--)
                {
                    if (char.IsWhiteSpace(text[j]))
                    {
                        breakAt = j;
                        break;
                    }
                }

                if (breakAt > lineStart)
                {
                    lineEnd = breakAt;
                    i = breakAt + 1; // skip the whitespace so next line starts clean
                }
            }

            lines.Add(text[lineStart..lineEnd]);
        }

        return lines;
    }
}

namespace ArayCode.Formatting;

/// <summary>
/// Validates Spectre.Console markup tags and style tokens.
/// </summary>
public static class SpectreColorValidator
{
    /// <summary>
    /// Returns true if <paramref name="tag"/> is a known Spectre.Console
    /// tag/style. Used for rejecting improbable tag names that appeared
    /// after whitespace in content.
    /// </summary>
    public static bool IsKnownColorTag(string tag)
    {
        if (string.IsNullOrEmpty(tag))
            return false;

        // Hex colors like #1a3a1a, #ff00ff are always valid Spectre tags
        if (tag.Contains('#'))
            return true;

        // Strip any style attributes like "on color" or "link=url"
        if (tag.StartsWith("link=", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("link ", StringComparison.OrdinalIgnoreCase))
            return true;

        // For combined styles like "bold on blue" or "lime on #1a3a1a",
        // check if there's an "on" keyword separating two style tokens.
        int spaceIdx = tag.IndexOf(' ');
        if (spaceIdx > 0)
        {
            int onIdx = tag.IndexOf(" on ", StringComparison.OrdinalIgnoreCase);
            if (onIdx > 0)
            {
                string beforeOn = tag.Substring(0, onIdx);
                string afterOn = tag.Substring(onIdx + 4);
                return IsKnownStyleToken(beforeOn) && IsKnownStyleToken(afterOn);
            }

            // Without "on", it may be a combined style like "bold italic".
            // Split by spaces and check each token individually.
            string[] tokens = tag.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool allKnown = true;
            foreach (var t in tokens)
            {
                if (!IsKnownStyleToken(t)) { allKnown = false; break; }
            }
            if (allKnown) return true;
        }

        string baseName = spaceIdx >= 0 ? tag.Substring(0, spaceIdx) : tag;
        return SpectreColorPalette.IsValidColor(baseName);
    }

    /// <summary>
    /// Checks whether a style token string (which may contain multiple
    /// space-separated tokens like "bold gray89") is valid.
    /// Supports hex colors and known decoration/color keywords.
    /// </summary>
    public static bool IsKnownStyleToken(string token)
    {
        token = token.Trim();
        if (string.IsNullOrEmpty(token))
            return false;

        // If the token contains spaces, split and validate each part
        int spaceIdx = token.IndexOf(' ');
        if (spaceIdx >= 0)
        {
            string[] parts = token.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (!IsKnownSingleStyleToken(part))
                    return false;
            }
            return true;
        }

        return IsKnownSingleStyleToken(token);
    }

    /// <summary>
    /// Checks a single style token (no spaces) against known Spectre keywords.
    /// Hex colors and named colors/decorations are accepted.
    /// </summary>
    public static bool IsKnownSingleStyleToken(string token)
    {
        token = token.Trim();
        if (string.IsNullOrEmpty(token))
            return false;
        // Hex color
        if (token.StartsWith('#'))
            return true;
        // Known keyword
        return SpectreColorPalette.IsValidColor(token);
    }
}

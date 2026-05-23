namespace ArayCode;

/// <summary>
/// Result of validating a Spectre.Console markup tag.
/// </summary>
public readonly struct ValidationResult
{
    /// <summary>
    /// True if the tag content is valid Spectre.Console markup.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// The normalized tag content (e.g., "link=url" with spaces removed around '=').
    /// </summary>
    public string NormalizedContent { get; }

    /// <summary>
    /// True if the tag should be escaped (rendered as literal brackets).
    /// </summary>
    public bool ShouldEscape { get; }

    public ValidationResult(bool isValid, string normalizedContent, bool shouldEscape = false)
    {
        IsValid = isValid;
        NormalizedContent = normalizedContent;
        ShouldEscape = shouldEscape;
    }

    public static ValidationResult Valid(string normalizedContent) => new(true, normalizedContent);
    public static ValidationResult Invalid(string content) => new(false, content);
    public static ValidationResult Escape(string content) => new(false, content, true);
}

/// <summary>
/// Validates and normalizes Spectre.Console markup tags.
/// </summary>
public static class SpectreMarkupValidator
{
    /// <summary>
    /// Returns true if <paramref name="tagContent"/> looks like a valid
    /// Spectre.Console tag name (alphanumeric, hyphens, dots, underscores,
    /// color names, and spaces for compound tags like "bold underline").
    /// Tags containing quotes or other special characters are likely
    /// literal bracket content.
    /// </summary>
    public static bool IsValidTagName(string tagContent)
    {
        if (string.IsNullOrEmpty(tagContent))
            return false;

        foreach (char ch in tagContent)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '-' && ch != '.' && ch != '_' && ch != '#' && ch != ' ' && ch != '=' && ch != ':' && ch != '/')
                return false;
        }
        return true;
    }

    /// <summary>
    /// Normalizes tag content to match Spectre.Console's expected format.
    /// Spectre requires link=url without spaces around the '=', but agent
    /// output may contain a space before '=' (e.g. "link = url").
    /// This normalization strips spaces adjacent to '=' so the tag is valid.
    /// </summary>
    public static string NormalizeTagContent(string tagContent)
    {
        if (string.IsNullOrEmpty(tagContent))
            return tagContent;

        int eqIdx = tagContent.IndexOf('=');
        if (eqIdx < 0)
            return tagContent;

        // Only normalize if there are spaces before '='
        if (eqIdx > 0 && tagContent[eqIdx - 1] == ' ')
        {
            // Remove spaces immediately before '='
            int trimEnd = eqIdx - 1;
            while (trimEnd >= 0 && tagContent[trimEnd] == ' ')
                trimEnd--;

            // Also remove spaces immediately after '='
            int trimStart = eqIdx + 1;
            while (trimStart < tagContent.Length && tagContent[trimStart] == ' ')
                trimStart++;

            // Rebuild: part before spaces + '=' + part after spaces
            string before = tagContent.Substring(0, trimEnd + 1);
            string after = tagContent.Substring(trimStart);
            return before + "=" + after;
        }

        return tagContent;
    }

    /// <summary>
    /// Returns true if <paramref name="tagName"/> is a known Spectre.Console
    /// tag/style. Only used for rejecting improbable tag names that appeared
    /// after whitespace in content.
    /// </summary>
    public static bool IsSpectreKnownTag(string tagName)
    {
        return Formatting.SpectreColorValidator.IsKnownColorTag(tagName);
    }

    /// <summary>
    /// Validates tag content and returns a <see cref="ValidationResult"/>.
    /// Checks if the content is a valid Spectre tag or should be escaped.
    /// </summary>
    public static ValidationResult ValidateTagContent(string tagContent)
    {
        // Single-character tag names (like [x], [b], [5]) are almost certainly
        // literal code content (array access, variable names), not intentional markup.
        if (tagContent.Length <= 1 && tagContent != "/")
        {
            return ValidationResult.Escape(tagContent);
        }

        // Generic close tag is always valid
        if (tagContent == "/")
        {
            return ValidationResult.Valid(tagContent);
        }

        // Explicit close tags like [/dim] - check validity
        if (tagContent.StartsWith("/"))
        {
            string closeTagName = tagContent.Substring(1);
            if (!IsValidTagName(closeTagName) || !IsSpectreKnownTag(closeTagName))
            {
                return ValidationResult.Escape(tagContent);
            }
            return ValidationResult.Valid(tagContent);
        }

        // Opening tag validation
        if (!IsValidTagName(tagContent))
        {
            return ValidationResult.Escape(tagContent);
        }

        if (!IsSpectreKnownTag(tagContent))
        {
            return ValidationResult.Escape(tagContent);
        }

        // Normalize and return
        string normalized = NormalizeTagContent(tagContent);
        return ValidationResult.Valid(normalized);
    }
}

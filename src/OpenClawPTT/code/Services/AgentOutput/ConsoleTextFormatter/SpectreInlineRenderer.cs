using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// Handles inline Markdown-to-Spectre conversion: bold, italic, code, links, etc.
/// This class is used by <see cref="MarkdownToSpectreConverter"/> during block-level
/// parsing and by <see cref="SpectreTableRenderer"/> for cell content.
/// </summary>
internal static class SpectreInlineRenderer
{
    // ── Inline patterns (applied in order — order matters) ──────────────────

    // Bold + italic must come before bold and italic individually.
    private static readonly Regex BoldItalicStars = new(@"\*\*\*(.+?)\*\*\*", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex BoldStars = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex BoldUnderscores = new(@"__(.+?)__", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex ItalicStars = new(@"\*(.+?)\*", RegexOptions.Compiled | RegexOptions.Singleline);

    // Underscore-italic: only match when surrounded by word boundaries to avoid
    // false positives inside snake_case identifiers.
    private static readonly Regex ItalicUnderscores = new(@"(?<!\w)_(.+?)_(?!\w)", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex Strikethrough = new(@"~~(.+?)~~", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex InlineCode = new(@"`(.+?)`", RegexOptions.Compiled | RegexOptions.Singleline);

    // Markdown link: [label](url)
    private static readonly Regex Link = new(@"\[([^\]]+)\]\(([^)]+)\)", RegexOptions.Compiled);

    // ── Placeholder tokens for inline code protection ───────────────────────
    // These are unlikely to appear in real markdown input.
    private const string CodePlaceholderPrefix = "\x00CODE";
    private const string CodePlaceholderSuffix = "\x00";

    /// <summary>
    /// Converts inline Markdown formatting to Spectre.Console markup.
    /// Handles: bold, italic, bold+italic, strikethrough, inline code, and links.
    /// </summary>
    public static string ConvertInline(string text)
    {
        text = EscapeBracketsExceptLinks(text);

        var codePlaceholders = new Dictionary<int, string>();
        int codeIdx = 0;
        text = InlineCode.Replace(text, m =>
        {
            string content = m.Groups[1].Value;
            int idx = codeIdx++;
            codePlaceholders[idx] = content;
            return CodePlaceholderPrefix + idx + CodePlaceholderSuffix;
        });

        text = ConvertLinksWithFormatting(text);
        text = ApplyInlineFormatting(text);

        for (int i = 0; i < codeIdx; i++)
        {
            text = text.Replace(
                CodePlaceholderPrefix + i + CodePlaceholderSuffix,
                "[bold gray89 on darkblue]" + codePlaceholders[i] + "[/]");
        }

        return text;
    }

    /// <summary>
    /// Applies bold, italic, bold-italic, and strikethrough formatting patterns.
    /// </summary>
    private static string ApplyInlineFormatting(string text)
    {
        text = BoldItalicStars.Replace(text, "[bold italic]$1[/]");
        text = BoldStars.Replace(text, "[bold]$1[/]");
        text = BoldUnderscores.Replace(text, "[bold]$1[/]");
        text = ItalicStars.Replace(text, "[italic]$1[/]");
        text = ItalicUnderscores.Replace(text, "[italic]$1[/]");
        text = Strikethrough.Replace(text, "[strikethrough]$1[/]");
        return text;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Bracket escaping helpers
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Escapes square brackets for Spectre.Console by doubling them.
    /// </summary>
    public static string EscapeBrackets(string text)
        => text.Replace("[", "[[").Replace("]", "]]");

    /// <summary>
    /// Escapes brackets but preserves Markdown links (which produce valid Spectre link markup).
    /// </summary>
    private static string EscapeBracketsExceptLinks(string text)
    {
        var placeholders = new List<string>();

        string protected_ = Link.Replace(text, m =>
        {
            int idx = placeholders.Count;
            placeholders.Add(m.Value);
            return $"\x00LINK{idx}\x00";
        });

        protected_ = protected_.Replace("[", "[[").Replace("]", "]]");

        for (int i = 0; i < placeholders.Count; i++)
            protected_ = protected_.Replace($"\x00LINK{i}\x00", placeholders[i]);

        return protected_;
    }

    /// <summary>
    /// Converts Markdown links [label](url) to Spectre.Console link markup.
    /// Handles inline formatting within the label text.
    /// </summary>
    private static string ConvertLinksWithFormatting(string text)
    {
        return Link.Replace(text, m =>
        {
            string label = m.Groups[1].Value;
            string url = m.Groups[2].Value;

            string formattedLabel = ApplyInlineFormatting(label);

            var outerTagMatch = Regex.Match(
                formattedLabel, @"^\[([a-z0-9 ]+)\](.+)\[/\]$", RegexOptions.Singleline);

            if (outerTagMatch.Success)
            {
                string style = outerTagMatch.Groups[1].Value;
                string inner = outerTagMatch.Groups[2].Value;
                return $"[{style} link={url}]{inner}[/]";
            }

            return $"[link={url}]{formattedLabel}[/]";
        });
    }
}

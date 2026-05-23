using System.Text;
using System.Text.RegularExpressions;

namespace ArayCode.Services;

/// <summary>
/// Sanitizes text before sending to an LLM, removing markdown, code blocks,
/// URLs, excessive whitespace, and other noise unsuitable for TTS or summarization.
/// </summary>
public static class TtsContentFilter
{
    // ── Options ────────────────────────────────────────────────────────────────

    public enum CodeBlockMode { Summarize, Skip, Smart, Describe }

    public record SanitizerOptions
    {
        public CodeBlockMode CodeBlockMode { get; init; } = CodeBlockMode.Smart;
        public bool RemoveUrls { get; init; } = true;
        public bool RemoveEmoji { get; init; } = true;
        public bool RemoveHtml { get; init; } = true;
        public bool CollapseWhitespace { get; init; } = true;
        /// <summary>Max chars to send to LLM. 0 = no limit.</summary>
        public int MaxLength { get; init; } = 0;
    }

    // ── Public entry point ─────────────────────────────────────────────────────

    public static string SanitizeForTts(string text, SanitizerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        options ??= new SanitizerOptions();

        var sb = new StringBuilder(text);

        // Order matters: code blocks first, then inline code, then the rest
        ProcessCodeBlocks(sb, options.CodeBlockMode);
        StripInlineCode(sb);

        if (options.RemoveHtml) StripHtml(sb);
        if (options.RemoveUrls) StripUrls(sb);

        StripMarkdown(sb);

        if (options.RemoveEmoji) StripEmoji(sb);
        if (options.CollapseWhitespace) CollapseWhitespace(sb);

        var result = sb.ToString().Trim();

        if (options.MaxLength > 0 && result.Length > options.MaxLength)
            result = result[..options.MaxLength].TrimEnd() + "…";

        return result;
    }

    // ── Code blocks ────────────────────────────────────────────────────────────

    private static readonly Regex CodeBlockRegex = new(
        @"```(?<lang>\w+)?\s*\n?(?<code>[\s\S]*?)```",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static void ProcessCodeBlocks(StringBuilder sb, CodeBlockMode mode)
    {
        var text = sb.ToString();

        var result = CodeBlockRegex.Replace(text, match =>
        {
            var lang = match.Groups["lang"].Value.Trim();
            var code = match.Groups["code"].Value.Trim();
            var lineCount = code.Split('\n').Length;

            return mode switch
            {
                CodeBlockMode.Skip =>
                    string.IsNullOrEmpty(lang)
                        ? "[Code block]"
                        : $"[{lang} code block]",

                CodeBlockMode.Summarize =>
                    $"[Code block{(string.IsNullOrEmpty(lang) ? "" : $" in {lang}")}]",

                CodeBlockMode.Smart =>
                    lineCount < 5
                        ? $"[Short {(string.IsNullOrEmpty(lang) ? "code" : lang)} snippet]"
                        : $"[{(string.IsNullOrEmpty(lang) ? "Code" : char.ToUpper(lang[0]) + lang[1..])} block]",

                CodeBlockMode.Describe or _ =>
                    $"[Code block{(string.IsNullOrEmpty(lang) ? "" : $" in {lang}")}]",
            };
        });

        sb.Clear();
        sb.Append(result);
    }

    // ── Inline code ────────────────────────────────────────────────────────────

    private static readonly Regex InlineCodeRegex = new(
        @"`([^`]+)`",
        RegexOptions.Compiled);

    private static void StripInlineCode(StringBuilder sb)
    {
        // Keep the text, just remove the backtick delimiters
        var result = InlineCodeRegex.Replace(sb.ToString(), m => m.Groups[1].Value);
        sb.Clear();
        sb.Append(result);
    }

    // ── HTML ───────────────────────────────────────────────────────────────────

    private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex HtmlEntityRegex = new(@"&(?:#\d+|#x[\da-fA-F]+|\w+);", RegexOptions.Compiled);

    private static void StripHtml(StringBuilder sb)
    {
        var s = HtmlTagRegex.Replace(sb.ToString(), " ");
        s = HtmlEntityRegex.Replace(s, m => DecodeHtmlEntity(m.Value));
        sb.Clear();
        sb.Append(s);
    }

    private static string DecodeHtmlEntity(string entity) => entity switch
    {
        "&amp;" => "&",
        "&lt;" => "<",
        "&gt;" => ">",
        "&quot;" => "\"",
        "&apos;" => "'",
        "&nbsp;" => " ",
        _ => " "
    };

    // ── URLs ───────────────────────────────────────────────────────────────────

    private static readonly Regex UrlRegex = new(
        @"https?://\S+|www\.\S+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static void StripUrls(StringBuilder sb)
    {
        var result = UrlRegex.Replace(sb.ToString(), "[link]");
        sb.Clear();
        sb.Append(result);
    }

    // ── Markdown ───────────────────────────────────────────────────────────────

    private static readonly (Regex Pattern, string Replacement)[] MarkdownRules =
    [
        // Headings  ## Heading → Heading
        (new Regex(@"^#{1,6}\s+", RegexOptions.Multiline | RegexOptions.Compiled), ""),

        // Bold+italic  ***text***
        (new Regex(@"\*{3}(.+?)\*{3}", RegexOptions.Compiled | RegexOptions.Singleline), "$1"),
        // Bold  **text**
        (new Regex(@"\*{2}(.+?)\*{2}", RegexOptions.Compiled | RegexOptions.Singleline), "$1"),
        // Italic  *text*
        (new Regex(@"\*(.+?)\*",       RegexOptions.Compiled | RegexOptions.Singleline), "$1"),
        // Bold  __text__
        (new Regex(@"_{2}(.+?)_{2}",   RegexOptions.Compiled | RegexOptions.Singleline), "$1"),
        // Italic  _text_
        (new Regex(@"_(.+?)_",         RegexOptions.Compiled | RegexOptions.Singleline), "$1"),
        // Orphaned leftover asterisks/underscores (e.g. stray ** with no closing)
        (new Regex(@"[\*_]+",          RegexOptions.Compiled), ""),

        // Strikethrough  ~~text~~
        (new Regex(@"~~(.+?)~~", RegexOptions.Compiled), "$1"),

        // Markdown images  ![alt](url)  →  alt
        (new Regex(@"!\[([^\]]*)\]\([^\)]*\)", RegexOptions.Compiled), "$1"),

        // Markdown links  [text](url)  →  text
        (new Regex(@"\[([^\]]+)\]\([^\)]+\)", RegexOptions.Compiled), "$1"),

        // Bare reference links  [text][ref]  →  text
        (new Regex(@"\[([^\]]+)\]\[[^\]]*\]", RegexOptions.Compiled), "$1"),

        // Blockquotes  > text
        (new Regex(@"^>\s*", RegexOptions.Multiline | RegexOptions.Compiled), ""),

        // Horizontal rules  ---, ***, ___
        (new Regex(@"^[-*_]{3,}\s*$", RegexOptions.Multiline | RegexOptions.Compiled), ""),

        // Unordered list bullets  - item, * item, + item
        (new Regex(@"^[\s]*[-*+]\s+", RegexOptions.Multiline | RegexOptions.Compiled), ""),

        // Ordered list numbers  1. item
        (new Regex(@"^[\s]*\d+\.\s+", RegexOptions.Multiline | RegexOptions.Compiled), ""),

        // Tables: strip pipe characters and separator rows
        (new Regex(@"^\|[-| :]+\|$", RegexOptions.Multiline | RegexOptions.Compiled), ""),
        (new Regex(@"\|", RegexOptions.Compiled), " "),

        // YAML front matter  --- ... ---
        (new Regex(@"^---[\s\S]*?---\s*", RegexOptions.Compiled), ""),
    ];

    private static void StripMarkdown(StringBuilder sb)
    {
        var s = sb.ToString();
        foreach (var (pattern, replacement) in MarkdownRules)
            s = pattern.Replace(s, replacement);
        sb.Clear();
        sb.Append(s);
    }

    // ── Emoji ──────────────────────────────────────────────────────────────────

    private static readonly Regex EmojiRegex = new(
        @"[\u2600-\u27BF]|[\uD83C-\uDBFF][\uDC00-\uDFFF]|\u00A9|\u00AE|" +
        @"[\u2000-\u3300]|\uFE0F|\u200D",
        RegexOptions.Compiled);

    private static void StripEmoji(StringBuilder sb)
    {
        var result = EmojiRegex.Replace(sb.ToString(), "");
        sb.Clear();
        sb.Append(result);
    }

    // ── Whitespace ─────────────────────────────────────────────────────────────

    private static readonly Regex MultipleSpaces = new(@"[ \t]{2,}", RegexOptions.Compiled);
    private static readonly Regex MultipleNewlines = new(@"\n{3,}", RegexOptions.Compiled);

    private static void CollapseWhitespace(StringBuilder sb)
    {
        var s = MultipleSpaces.Replace(sb.ToString(), " ");
        s = MultipleNewlines.Replace(s, "\n\n");
        sb.Clear();
        sb.Append(s);
    }

    // ── Special formatting detection ───────────────────────────────────────────

    private static readonly Regex HeadingPattern = new(
        @"^#{1,6}\s", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// Checks if text contains markdown or code that needs special handling.
    /// Uses <c>^#{1,6} </c> regex for headings instead of bare <c>#</c> to avoid
    /// false positives on plain hashtags like <c>#topic</c>.
    /// </summary>
    public static bool HasSpecialFormatting(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        return text.Contains("```") ||
               text.Contains("`") ||
               text.Contains("**") ||
               HeadingPattern.IsMatch(text) ||
               text.Contains("|") ||
               text.Contains("http");
    }

    /// <summary>
    /// Truncates text to max length with ellipsis.
    /// </summary>
    public static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        if (maxLength <= 4)
            return text[..maxLength];

        return text[..(maxLength - 4)] + " ...";
    }
}
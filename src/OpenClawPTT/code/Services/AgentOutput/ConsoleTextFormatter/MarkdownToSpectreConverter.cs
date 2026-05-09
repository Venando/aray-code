using System;
using System.Text;
using System.Text.RegularExpressions;
using static SpectreInlineRenderer;

/// <summary>
/// Converts a Markdown string (.md) into an equivalent Spectre.Console markup string.
/// </summary>
/// <remarks>
/// Supported Markdown constructs:
///   Headings        # H1  ## H2  ### H3+
///   Bold            **text**  __text__
///   Italic          *text*  _text_
///   Bold+Italic     ***text***
///   Strikethrough   ~~text~~
///   Inline code     `code`
///   Links           [label](url)
///   Blockquotes     > text
///   Thematic break  --- or *** or ___ (on its own line)
///   Tables          | a | b |
///                   |---|---|
///                   | 1 | 2 |
/// </remarks>
public static class MarkdownToSpectreConverter
{
    // ── Block patterns ────────────────────────────────────────────────────────

    private static readonly Regex HeadingPattern = new(@"^(#{1,6})\s+(.*)", RegexOptions.Compiled);
    private static readonly Regex BlockquotePattern = new(@"^>\s?(.*)", RegexOptions.Compiled);
    private static readonly Regex HrPattern = new(@"^(\*{3,}|-{3,}|_{3,})\s*$", RegexOptions.Compiled);
    private static readonly Regex FencePattern = new(@"^```", RegexOptions.Compiled);

    /// <summary>
    /// Converts <paramref name="markdown"/> to a Spectre.Console markup string
    /// with an available width for table layout (to avoid overflowing the console).
    /// </summary>
    public static string Convert(string markdown, int availableWidth = int.MaxValue)
    {
        if (markdown is null) throw new ArgumentNullException(nameof(markdown));
        if (availableWidth <= 0) availableWidth = int.MaxValue;

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var result = new StringBuilder();

        bool inFencedBlock = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            // ── Fenced code block ────────────────────────────────────────────
            if (FencePattern.IsMatch(line))
            {
                inFencedBlock = !inFencedBlock;

                if (inFencedBlock)
                    result.MyAppendLine("[dim]─────────────────[italic]code[/]─────────────────[/]");
                else
                    result.MyAppendLine("[dim]──────────────────────────────────────[/]");

                continue;
            }

            if (inFencedBlock)
            {
                result.MyAppendLine($"[default on gray15]{EscapeBrackets(line)}[/]");
                continue;
            }

            // ── Table ────────────────────────────────────────────────────────
            // Check if this line starts a table (current line starts with |, next is separator).
            if (line.Length > 0 && line[0] == '|' && i + 1 < lines.Length &&
                SpectreTableRenderer.IsTableSeparator(lines[i + 1]))
            {
                i = SpectreTableRenderer.RenderTable(lines, i, result, availableWidth);
                continue;
            }

            // ── Thematic break (--- / *** / ___) ────────────────────────────
            if (HrPattern.IsMatch(line))
            {
                result.MyAppendLine("[dim]────────────────────────────────────────[/]");
                continue;
            }

            // ── Headings ─────────────────────────────────────────────────────
            var headingMatch = HeadingPattern.Match(line);
            if (headingMatch.Success)
            {
                int level = headingMatch.Groups[1].Value.Length;
                string content = SpectreInlineRenderer.ConvertInline(headingMatch.Groups[2].Value);

                string spectreTag = level switch
                {
                    1 => $"[bold underline default on gray27]{content}[/]",
                    2 => $"[bold underline]{content}[/]",
                    _ => $"[bold dim]{content}[/]",   // H3–H6
                };

                result.MyAppendLine(spectreTag);
                continue;
            }

            // ── Blockquote ───────────────────────────────────────────────────
            var bqMatch = BlockquotePattern.Match(line);
            if (bqMatch.Success)
            {
                string content = SpectreInlineRenderer.ConvertInline(bqMatch.Groups[1].Value);
                result.MyAppendLine($"[italic dim]{content}[/]");
                continue;
            }

            // ── Normal paragraph line ────────────────────────────────────────
            result.MyAppendLine(SpectreInlineRenderer.ConvertInline(line));
        }

        return result.ToString().TrimEnd();
    }
}

/// <summary>
/// Extension methods for StringBuilder used by the Markdown-to-Spectre pipeline.
/// </summary>
internal static class StringBuilderExtensions
{
    public static StringBuilder MyAppendLine(this StringBuilder sb)
        => sb.Append('\n');

    public static StringBuilder MyAppendLine(this StringBuilder sb, string line)
        => sb.Append(line + "\n");
}

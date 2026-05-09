using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;
using OpenClawPTT;

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
/// <summary>
/// Per-column alignment for markdown tables.
/// </summary>
internal enum TableAlignment
{
    Left,
    Center,
    Right,
}

public static class MarkdownToSpectreConverter
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

    // ── Block patterns (applied per-line) ────────────────────────────────────

    private static readonly Regex HeadingPattern = new(@"^(#{1,6})\s+(.*)", RegexOptions.Compiled);
    private static readonly Regex BlockquotePattern = new(@"^>\s?(.*)", RegexOptions.Compiled);
    private static readonly Regex HrPattern = new(@"^(\*{3,}|-{3,}|_{3,})\s*$", RegexOptions.Compiled);
    // Fenced code block delimiter: ``` optionally followed by a language name.
    private static readonly Regex FencePattern = new(@"^```", RegexOptions.Compiled);
    // Table delimiter line: |---|---| pattern
    private static readonly Regex TableSeparatorPattern = new(@"^\|[-:\s|]+\|$", RegexOptions.Compiled);
    // Table row: | cells |
    private static readonly Regex TableRowPattern = new(@"^\|.+\|$", RegexOptions.Compiled);

    // ── Placeholder tokens for inline code protection ───────────────────────
    // These are unlikely to appear in real markdown input.
    private const string CodePlaceholderPrefix = "\x00CODE";
    private const string CodePlaceholderSuffix = "\x00";

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
            if (TableRowPattern.IsMatch(line) && i + 1 < lines.Length && TableSeparatorPattern.IsMatch(lines[i + 1]))
            {
                i = RenderTable(lines, i, result, availableWidth);
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
                string content = ConvertInline(headingMatch.Groups[2].Value);

                string spectreTag = level switch
                {
                    1 => $"[bold underline]{content}[/]",
                    2 => $"[bold]{content}[/]",
                    _ => $"[bold dim]{content}[/]",   // H3–H6
                };

                result.MyAppendLine(spectreTag);
                continue;
            }

            // ── Blockquote ───────────────────────────────────────────────────
            var bqMatch = BlockquotePattern.Match(line);
            if (bqMatch.Success)
            {
                string content = ConvertInline(bqMatch.Groups[1].Value);
                result.MyAppendLine($"[italic dim]{content}[/]");
                continue;
            }

            // ── Normal paragraph line ────────────────────────────────────────
            result.MyAppendLine(ConvertInline(line));
        }

        return result.ToString().TrimEnd();
    }

    // ────────────────────────────────────────────────────────────────────────
    // Table rendering
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Represents a single parsed markdown table.
    /// </summary>
    private sealed class MarkdownTable
    {
        public int ColumnCount { get; set; }
        public List<TableAlignment> Alignments { get; } = new();
        public List<List<string>> Rows { get; } = new();
        public List<List<string>> FormattedRows { get; } = new();
    }

    /// <summary>
    /// Parses the separator line to determine per-column alignment.
    /// </summary>
    private static TableAlignment ParseAlignment(string cellText)
    {
        string trimmed = cellText.Trim();
        bool left = trimmed.StartsWith(':');
        bool right = trimmed.EndsWith(':');

        if (left && right) return TableAlignment.Center;
        if (right) return TableAlignment.Right;
        return TableAlignment.Left;
    }

    /// <summary>
    /// Parses a table row (header or body) into individual cell values.
    /// Strips leading/trailing pipe and splits on internal pipes.
    /// </summary>
    private static string[] ParseRowCells(string line)
    {
        // Remove leading and trailing whitespace+pipe, then split on '|'
        string trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed.Substring(1);
        if (trimmed.EndsWith('|')) trimmed = trimmed.Substring(0, trimmed.Length - 1);

        return trimmed.Split('|');
    }

    /// <summary>
    /// Gets the display width of cell content with Spectre markup stripped.
    /// Uses CharacterWidth for accurate East Asian character width measurement.
    /// </summary>
    private static int GetCellDisplayWidth(string formattedCell)
    {
        string plain = Markup.Remove(formattedCell);
        return CharacterWidth.GetDisplayWidth(plain);
    }

    /// <summary>
    /// Pads a cell's formatted content to the target display width.
    /// Accounts for East Asian full-width characters when determining padding.
    /// </summary>
    private static string PadCell(string formattedCell, int targetDisplayWidth)
    {
        int currentWidth = GetCellDisplayWidth(formattedCell);
        int padding = targetDisplayWidth - currentWidth;
        if (padding <= 0) return formattedCell;

        return formattedCell + new string(' ', padding);
    }

    /// <summary>
    /// Renders a markdown table starting at <paramref name="startIndex"/>.
    /// Returns the index of the last processed line.
    /// </summary>
    private static int RenderTable(string[] lines, int startIndex, StringBuilder result, int availableWidth)
    {
        // Collect all table lines
        var tableLines = new List<string>();
        int i = startIndex;

        while (i < lines.Length && TableRowPattern.IsMatch(lines[i]))
        {
            tableLines.Add(lines[i]);
            i++;
        }

        if (tableLines.Count < 2)
        {
            // Not enough lines for a valid table (needs header + separator)
            result.MyAppendLine(ConvertInline(lines[startIndex]));
            return startIndex;
        }

        // Parse table
        var table = new MarkdownTable();

        // Separator is line 1 (index 1)
        string separatorLine = tableLines[1];
        string[] separatorCells = ParseRowCells(separatorLine);

        // Parse header (line 0) and body rows (line 2+)
        string[] headerCells = ParseRowCells(tableLines[0]);
        table.ColumnCount = Math.Max(headerCells.Length, separatorCells.Length);

        // Parse alignments from separator
        for (int c = 0; c < table.ColumnCount; c++)
        {
            string sepCell = c < separatorCells.Length ? separatorCells[c].Trim() : "---";
            table.Alignments.Add(ParseAlignment(sepCell));
        }

        // Parse and format header
        var headerFormatted = new List<string>();
        for (int c = 0; c < table.ColumnCount; c++)
        {
            string raw = c < headerCells.Length ? headerCells[c].Trim() : "";
            headerFormatted.Add(ConvertInline(raw));
        }
        table.Rows.Add(headerCells.Select(c => c.Trim()).ToList());
        table.FormattedRows.Add(headerFormatted);

        // Parse and format body rows
        for (int r = 2; r < tableLines.Count; r++)
        {
            string[] cells = ParseRowCells(tableLines[r]);
            var rowCells = new List<string>();
            var rowFormatted = new List<string>();
            for (int c = 0; c < table.ColumnCount; c++)
            {
                string raw = c < cells.Length ? cells[c].Trim() : "";
                rowCells.Add(raw);
                rowFormatted.Add(ConvertInline(raw));
            }
            table.Rows.Add(rowCells);
            table.FormattedRows.Add(rowFormatted);
        }

        // Calculate column widths
        int[] colWidths = new int[table.ColumnCount];

        for (int c = 0; c < table.ColumnCount; c++)
        {
            int maxWidth = 0;
            foreach (var row in table.FormattedRows)
            {
                if (c < row.Count)
                {
                    int w = GetCellDisplayWidth(row[c]);
                    if (w > maxWidth) maxWidth = w;
                }
            }
            colWidths[c] = Math.Max(maxWidth, 1); // Minimum width of 1
        }

        // ── Check available width and shrink columns if needed ──
        // Table total = borders (1 left + 1 right) + padding (1 per side per col)
        // + separator (colCount - 1) internal separators
        // Visual: ┌─┬─┐ = 3 chars overhead + 2 per column padding + separator spacing
        // Simplified: totalWidth = 1 (left border) + sum(colWidths + 2) + (colCount - 1)
        // Actually: ┌──┬──┐ = border + padding(1 each side) + separator between columns
        // Let's be precise:
        // ┌─<pad>──<pad>┬─<pad>──<pad>┐
        // = 2 for outermost borders (┌ ┐) but represented as │ │ in body rows
        // Actually our rendering uses the same approach:
        //   │  cell1  │  cell2  │
        // That's: │ (1) + space(1) + content + space(1) + │ (1) + space(1) + content + space(1) + │ (1)
        // = 1 (left border) + colWidths sum + 2*colCount (space padding) + colCount (separators between columns)
        // Wait, let me think again.

        // Rendering format:
        // │  content  │  content  │
        // border(1) space(1) content(w) space(1) separator(1) space(1) content(w) space(1) border(1)
        // = 1 + 1 + w + 1 + (colCount - 1 separators: each is 1+1+1 = ` │ `) + 1
        // Total overhead = 2 (outer borders) + 2*colCount (padding) + colCount - 1 (separators)
        // = 3*colCount + 1
        // Hmm, let me just compute it directly.

        int totalTableWidth = 1; // Left border │
        for (int c = 0; c < table.ColumnCount; c++)
        {
            totalTableWidth += colWidths[c] + 2; // padding on both sides
            if (c < table.ColumnCount - 1)
                totalTableWidth += 1; // separator │
        }
        totalTableWidth += 1; // Right border │

        // If the table exceeds available width, shrink columns proportionally
        if (totalTableWidth > availableWidth && availableWidth > 10)
        {
            int excess = totalTableWidth - availableWidth;
            int totalContentWidth = colWidths.Sum();

            if (totalContentWidth > 0)
            {
                // First pass: shrink from rightmost, keeping minimum width of 3
                for (int c = colWidths.Length - 1; c >= 0 && excess > 0; c--)
                {
                    int shrink = Math.Min(excess, Math.Max(0, colWidths[c] - 3));
                    colWidths[c] -= shrink;
                    excess -= shrink;
                }
            }

            // Second pass: if still over, shrink all columns down to minimum 1
            if (excess > 0)
            {
                for (int c = 0; c < colWidths.Length && excess > 0; c++)
                {
                    int shrink = Math.Min(excess, Math.Max(0, colWidths[c] - 1));
                    colWidths[c] -= shrink;
                    excess -= shrink;
                }
            }

            // If somehow still over (shouldn't happen with min 1 per col),
            // just set narrow columns. This is a last resort.
            if (excess > 0)
            {
                for (int c = 0; c < colWidths.Length; c++)
                    colWidths[c] = Math.Max(1, colWidths[c] - (excess / colWidths.Length) - 1);
            }
        }

        // ── Render the table ──

        // Top border: ╭────┬────╮
        result.MyAppendLine(RenderBorder(colWidths, '╭', '┬', '╮'));

        // Header row: │ bold content │
        var headerFormattedRow = table.FormattedRows[0];
        result.MyAppendLine(RenderContentRow(headerFormattedRow, colWidths, table.Alignments, isHeader: true));

        // Separator: ├────┼────┤
        result.MyAppendLine(RenderBorder(colWidths, '├', '┼', '┤'));

        // Body rows
        bool hasBodyRows = table.FormattedRows.Count > 1;
        if (hasBodyRows)
        {
            for (int r = 1; r < table.FormattedRows.Count; r++)
            {
                result.MyAppendLine(RenderContentRow(table.FormattedRows[r], colWidths, table.Alignments, isHeader: false));
            }
        }

        // Bottom border: ╰────┴────╯
        result.MyAppendLine(RenderBorder(colWidths, '╰', '┴', '╯'));

        return i - 1; // Return the index of the last processed line
    }

    /// <summary>
    /// Renders a horizontal border row for the table.
    /// </summary>
    private static string RenderBorder(int[] colWidths, char left, char join, char right)
    {
        var sb = new StringBuilder();
        sb.Append(left);

        for (int c = 0; c < colWidths.Length; c++)
        {
            sb.Append('─', colWidths[c] + 2); // content width + padding on both sides
            if (c < colWidths.Length - 1)
                sb.Append(join);
        }

        sb.Append(right);
        return sb.ToString();
    }

    /// <summary>
    /// Renders a content row with proper padding and alignment.
    /// </summary>
    /// <summary>
    /// Truncates <paramref name="formattedCell"/> (Spectre markup) so its
    /// visible display width does not exceed <paramref name="maxWidth"/>.
    /// Appends "…" if truncation occurs.
    /// </summary>
    private static string TruncateCellToWidth(string formattedCell, int maxWidth)
    {
        if (string.IsNullOrEmpty(formattedCell) || maxWidth <= 0)
            return "";

        string plain = Markup.Remove(formattedCell);
        int width = CharacterWidth.GetDisplayWidth(plain);

        if (width <= maxWidth)
            return formattedCell;

        // Need to truncate. Walk characters until we reach maxWidth - 1 (for ellipsis).
        int targetWidth = Math.Max(1, maxWidth - 1); // 1 char for "…" if possible
        var truncated = new StringBuilder();
        int currentWidth = 0;

        foreach (char c in plain)
        {
            int charWidth = CharacterWidth.GetDisplayWidth(c);
            if (currentWidth + charWidth > targetWidth)
                break;
            truncated.Append(c);
            currentWidth += charWidth;
        }

        truncated.Append('…');
        return truncated.ToString();
    }

    private static string RenderContentRow(List<string> formattedCells, int[] colWidths, List<TableAlignment> alignments, bool isHeader)
    {
        var sb = new StringBuilder();
        sb.Append('│');

        for (int c = 0; c < colWidths.Length; c++)
        {
            string cellContent = c < formattedCells.Count ? formattedCells[c] : "";
            int cellDisplayWidth = GetCellDisplayWidth(cellContent);
            int padding = colWidths[c] - cellDisplayWidth;

            // Truncate if cell content exceeds column width
            string displayContent;
            if (cellDisplayWidth > colWidths[c])
            {
                displayContent = TruncateCellToWidth(cellContent, colWidths[c]);
                cellDisplayWidth = GetCellDisplayWidth(displayContent);
                padding = colWidths[c] - cellDisplayWidth;
            }
            else
            {
                displayContent = cellContent;
            }

            // Apply alignment
            sb.Append(' '); // Left padding (always 1)

            if (padding > 0)
            {
                if (c < alignments.Count && alignments[c] == TableAlignment.Right)
                {
                    sb.Append(' ', padding);
                    sb.Append(isHeader ? $"[bold]{displayContent}[/]" : displayContent);
                }
                else if (c < alignments.Count && alignments[c] == TableAlignment.Center)
                {
                    int leftPad = padding / 2;
                    int rightPad = padding - leftPad;
                    sb.Append(' ', leftPad);
                    sb.Append(isHeader ? $"[bold]{displayContent}[/]" : displayContent);
                    sb.Append(' ', rightPad);
                }
                else
                {
                    // Left-aligned (default)
                    sb.Append(isHeader ? $"[bold]{displayContent}[/]" : displayContent);
                    sb.Append(' ', padding);
                }
            }
            else
            {
                // Cell content fits exactly (or overflows minimally)
                sb.Append(isHeader ? $"[bold]{displayContent}[/]" : displayContent);
            }

            sb.Append(' '); // Right padding (always 1)
            sb.Append('│');
        }

        return sb.ToString();
    }

    private static StringBuilder MyAppendLine(this StringBuilder stringBuilder)
    {
        return stringBuilder.Append('\n');
    }

    private static StringBuilder MyAppendLine(this StringBuilder stringBuilder, string line)
    {
        return stringBuilder.Append(line + "\n");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Inline conversion
    // ────────────────────────────────────────────────────────────────────────

    private static string ConvertInline(string text)
    {
        // Step 0: Escape square brackets that are NOT part of markdown link
        //         syntax so Spectre treats them as literals.
        text = EscapeBracketsExceptLinks(text);

        // Step 1: Protect inline code (backtick) content so no other pattern
        //         touches it. Code placeholders are safe from all regexes.
        var codePlaceholders = new Dictionary<int, string>();
        int codeIdx = 0;
        text = InlineCode.Replace(text, m =>
        {
            string content = m.Groups[1].Value;
            int idx = codeIdx++;
            codePlaceholders[idx] = content;
            return CodePlaceholderPrefix + idx + CodePlaceholderSuffix;
        });

        // Step 2: Convert markdown links [label](url) to Spectre link markup.
        text = ConvertLinksWithFormatting(text);

        // Step 3: Apply formatting patterns (bold, italic, etc.) to the
        //         remaining text.
        text = ApplyInlineFormatting(text);

        // Step 4: Restore inline code as [bold gray89 on darkblue]content[/].
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
    /// Escapes all '[' and ']' as '[[' and ']]' so Spectre.Console treats
    /// them as literal characters.
    /// </summary>
    private static string EscapeBrackets(string text)
        => text.Replace("[", "[[").Replace("]", "]]");

    /// <summary>
    /// Escapes brackets that are NOT part of a Markdown link pattern
    /// <c>[label](url)</c>, so they render as literals in Spectre.Console
    /// while still allowing the Link regex to fire afterwards.
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
    /// Converts markdown links <c>[label](url)</c> to Spectre link markup,
    /// applying formatting patterns (bold, italic, etc.) to the label first.
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

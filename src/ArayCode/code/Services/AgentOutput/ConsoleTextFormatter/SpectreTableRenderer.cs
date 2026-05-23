using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;
using ArayCode;
using ArayCode.Services.Themes;
using static SpectreInlineRenderer;

/// <summary>
/// Per-column alignment for markdown tables.
/// </summary>
internal enum TableAlignment
{
    Left,
    Center,
    Right,
}

/// <summary>
/// Represents a single parsed markdown table with parsed and formatted rows.
/// </summary>
internal sealed class MarkdownTable
{
    public int ColumnCount { get; }
    public List<TableAlignment> Alignments { get; } = new();
    public List<List<string>> FormattedRows { get; } = new();

    public MarkdownTable(int columnCount)
    {
        ColumnCount = columnCount;
    }
}

/// <summary>
/// Renders Markdown tables as Spectre.Console markup with box-drawing characters,
/// word-aware cell wrapping, and proportional column width distribution.
/// Table edge colors are driven by <see cref="ThemeProvider.Current.Table.EdgeColor"/>.
/// </summary>
internal static class SpectreTableRenderer
{
    /// <summary>Gets the current table edge color from the active theme.</summary>
    private static string TableEdgesMarkup => ThemeProvider.Current.Table.EdgeColor;

    /// <summary>
    /// Minimum display width per column in characters. Prevents columns from being
    /// shrunk to unreadably narrow sizes even when the table is too wide.
    /// </summary>
    private const int MinColumnWidth = 8;

    internal static readonly Regex TableSeparatorPattern = new(@"^\|[-:\s|]+\|$", RegexOptions.Compiled);
    private static readonly Regex TableRowPattern = new(@"^\|.+\|$", RegexOptions.Compiled);

    /// <summary>
    /// Returns true if the given line matches a markdown table separator (e.g. |---|---|).
    /// </summary>
    public static bool IsTableSeparator(string line)
        => TableSeparatorPattern.IsMatch(line);

    /// <summary>
    /// Renders a markdown table starting at <paramref name="startIndex"/>.
    /// Returns the index of the last processed line.
    /// </summary>
    public static int RenderTable(string[] lines, int startIndex, StringBuilder result, int availableWidth)
    {
        var tableLines = CollectTableLines(lines, startIndex);

        if (tableLines.Count < 2)
        {
            result.MyAppendLine(SpectreInlineRenderer.ConvertInline(lines[startIndex]));
            return startIndex;
        }

        var table = ParseTable(tableLines);
        int[] colWidths = CalculateColumnWidths(table);
        colWidths = FitColumnWidths(colWidths, availableWidth);

        // Pre-compute wrapped lines for all rows so we can check line counts
        // before rendering.
        var headerLines = RenderContentRowLines(table.FormattedRows[0], colWidths, table.Alignments, isHeader: true);

        var allBodyLines = new List<List<string>>();
        for (int r = 1; r < table.FormattedRows.Count; r++)
            allBodyLines.Add(RenderContentRowLines(table.FormattedRows[r], colWidths, table.Alignments, isHeader: false));

        // If any row (header or body) has separatorFor+ physical lines, insert row
        // separators between body rows for readability.
        const int separatorFor = 1;
        bool useRowSeparators = headerLines.Count >= separatorFor;
        if (!useRowSeparators)
            foreach (var row in allBodyLines)
                if (row.Count >= separatorFor) { useRowSeparators = true; break; }

        result.MyAppendLine(RenderBorder(colWidths, '╭', '┬', '╮'));
        foreach (var line in headerLines)
            result.MyAppendLine(line);
        result.MyAppendLine(RenderBorder(colWidths, '├', '┼', '┤'));

        for (int r = 0; r < allBodyLines.Count; r++)
        {
            if (useRowSeparators && r > 0)
                result.MyAppendLine(RenderBorder(colWidths, '├', '┼', '┤'));

            foreach (var line in allBodyLines[r])
                result.MyAppendLine(line);
        }

        result.MyAppendLine(RenderBorder(colWidths, '╰', '┴', '╯'));

        return startIndex + tableLines.Count - 1;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Table collection and parsing
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Collects consecutive table rows (header + separator + body).
    /// </summary>
    private static List<string> CollectTableLines(string[] lines, int startIndex)
    {
        var tableLines = new List<string>();
        int i = startIndex;
        while (i < lines.Length && TableRowPattern.IsMatch(lines[i]))
        {
            tableLines.Add(lines[i]);
            i++;
        }
        return tableLines;
    }

    /// <summary>
    /// Parses collected table lines into a <see cref="MarkdownTable"/>.
    /// </summary>
    private static MarkdownTable ParseTable(List<string> tableLines)
    {
        string[] separatorCells = ParseRowCells(tableLines[1]);
        string[] headerCells = ParseRowCells(tableLines[0]);
        int columnCount = Math.Max(headerCells.Length, separatorCells.Length);

        var table = new MarkdownTable(columnCount);

        for (int c = 0; c < columnCount; c++)
        {
            string sepCell = c < separatorCells.Length ? separatorCells[c].Trim() : "---";
            table.Alignments.Add(ParseAlignment(sepCell));
        }

        // Parse and format header (line 0)
        var headerFormatted = new List<string>();
        for (int c = 0; c < columnCount; c++)
        {
            string raw = c < headerCells.Length ? headerCells[c].Trim() : "";
            headerFormatted.Add(SpectreInlineRenderer.ConvertInline(raw));
        }
        table.FormattedRows.Add(headerFormatted);

        // Parse and format body rows (line 2+)
        for (int r = 2; r < tableLines.Count; r++)
        {
            string[] cells = ParseRowCells(tableLines[r]);
            var rowFormatted = new List<string>();
            for (int c = 0; c < columnCount; c++)
            {
                string raw = c < cells.Length ? cells[c].Trim() : "";
                rowFormatted.Add(SpectreInlineRenderer.ConvertInline(raw));
            }
            table.FormattedRows.Add(rowFormatted);
        }

        return table;
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
        string trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed.Substring(1);
        if (trimmed.EndsWith('|')) trimmed = trimmed.Substring(0, trimmed.Length - 1);

        return trimmed.Split('|');
    }

    // ────────────────────────────────────────────────────────────────────────
    // Column width calculation
    // ────────────────────────────────────────────────────────────────────────

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
    /// Calculates per-column display widths from formatted table rows.
    /// Returns natural widths based on the widest cell in each column.
    /// </summary>
    private static int[] CalculateColumnWidths(MarkdownTable table)
    {
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
            colWidths[c] = Math.Max(maxWidth, 1);
        }
        return colWidths;
    }

    /// <summary>
    /// Computes the total rendered width of a table given column widths,
    /// including borders, padding (1 char each side), and column separators.
    /// </summary>
    private static int ComputeTableTotalWidth(int[] colWidths)
    {
        int total = 1; // Left border
        for (int c = 0; c < colWidths.Length; c++)
        {
            total += colWidths[c] + 2; // content + padding both sides
            if (c < colWidths.Length - 1)
                total += 1; // column separator
        }
        total += 1; // Right border
        return total;
    }

    /// <summary>
    /// Fits column widths to the available console width.
    /// Uses proportional shrinking when the table is too wide: wider columns
    /// give up more space than narrower ones. Ensures minimum column widths.
    /// </summary>
    private static int[] FitColumnWidths(int[] colWidths, int availableWidth)
    {
        int totalWidth = ComputeTableTotalWidth(colWidths);

        if (totalWidth <= availableWidth || availableWidth <= 10)
            return colWidths;

        int[] result = (int[])colWidths.Clone();
        int excess = totalWidth - availableWidth;

        // Calculate overhead per column (2 padding chars + 1 separator char)
        // and total content width.
        int totalContentWidth = 0;
        for (int c = 0; c < result.Length; c++)
            totalContentWidth += result[c];

        int remainingExcess = excess;

        // Distribute excess proportionally: wider columns contribute more.
        for (int c = 0; c < result.Length && remainingExcess > 0; c++)
        {
            double share = totalContentWidth > 0
                ? (double)result[c] / totalContentWidth
                : 1.0 / result.Length;

            int shrink = Math.Min(remainingExcess, Math.Max(0, result[c] - MinColumnWidth));
            int proportionalShrink = Math.Max(0, (int)Math.Floor(excess * share));

            int actualShrink = Math.Min(shrink, proportionalShrink);
            result[c] -= actualShrink;
            remainingExcess -= actualShrink;
        }

        // Pass 2: if still over, shave remaining excess evenly across all columns
        // (respecting MinColumnWidth).
        for (int c = 0; c < result.Length && remainingExcess > 0; c++)
        {
            int shrink = Math.Min(remainingExcess, Math.Max(0, result[c] - MinColumnWidth));
            result[c] -= shrink;
            remainingExcess -= shrink;
        }

        // Pass 3: absolute last resort — go below MinColumnWidth but keep at least 1
        if (remainingExcess > 0)
        {
            for (int c = 0; c < result.Length && remainingExcess > 0; c++)
            {
                int shrink = Math.Min(remainingExcess, result[c] - 1);
                result[c] -= shrink;
                remainingExcess -= shrink;
            }
        }

        return result;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Border rendering
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders a horizontal border row for the table.
    /// </summary>
    private static string RenderBorder(int[] colWidths, char left, char join, char right)
    {
        var sb = new StringBuilder();
        sb.Append($"[{TableEdgesMarkup}]");
        sb.Append(left);

        for (int c = 0; c < colWidths.Length; c++)
        {
            sb.Append('─', colWidths[c] + 2);
            if (c < colWidths.Length - 1)
                sb.Append(join);
        }

        sb.Append(right);
        sb.Append("[/]");
        return sb.ToString();
    }

    // ────────────────────────────────────────────────────────────────────────
    // Cell wrapping
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts uniform Spectre markup from a formatted cell when the cell
    /// has a simple structure: opening tag(s) + plain text + closing tag(s).
    /// If the cell has mixed/complex formatting, prefix/suffix remain empty
    /// and wrapping falls back to plain text.
    /// </summary>
    internal static void ExtractUniformMarkup(string formattedCell, out string prefix, out string suffix)
    {
        prefix = "";
        suffix = "";

        if (string.IsNullOrEmpty(formattedCell)) return;

        // Find consecutive opening Spectre tags at the start: [tag1][tag2]...
        // Break on escaped brackets ("[[") — they are literal "[" characters,
        // not markup tags.
        int prefixEnd = 0;
        int prefixTagCount = 0;
        while (prefixEnd < formattedCell.Length && formattedCell[prefixEnd] == '[')
        {
            // Escaped bracket "[[" — this is literal text, not a tag
            if (prefixEnd + 1 < formattedCell.Length && formattedCell[prefixEnd + 1] == '[')
                break;

            int closeIdx = formattedCell.IndexOf(']', prefixEnd + 1);
            if (closeIdx < 0) break;
            prefixEnd = closeIdx + 1;
            prefixTagCount++;
        }

        // Find consecutive [/] at the end, but only consume up to the number
        // of opening tags found at the start. Inner tags (e.g. from inline code
        // like [bold gray89 on darkblue]) also add [/] closers — we must not
        // consume those because they don't match a prefix tag.
        int suffixStart = formattedCell.Length;
        int suffixTagCount = 0;
        while (suffixStart >= 3 && suffixTagCount < prefixTagCount)
        {
            if (formattedCell[suffixStart - 1] == ']' &&
                formattedCell[suffixStart - 3] == '[' &&
                formattedCell[suffixStart - 2] == '/')
            {
                suffixStart -= 3;
                suffixTagCount++;
            }
            else
                break;
        }

        if (prefixEnd == 0 || suffixStart == formattedCell.Length || prefixEnd >= suffixStart)
            return; // No uniform wrapping markup or prefix overlaps suffix

        try
        {
            string innerContent = formattedCell.Substring(prefixEnd, suffixStart - prefixEnd);
            string innerPlain = Markup.Remove(innerContent);
            string fullPlain = Markup.Remove(formattedCell);

            if (innerPlain == fullPlain)
            {
                prefix = formattedCell.Substring(0, prefixEnd);
                suffix = formattedCell.Substring(suffixStart);
            }
        }
        catch
        {
            // Malformed markup — fall back to no prefix/suffix rather than crash
        }
    }

    /// <summary>
    /// Wraps formatted cell content to fit <paramref name="maxWidth"/> by
    /// splitting into multiple display lines. Preserves uniform Spectre markup.
    /// Returns the cell as a single-element list if it fits without wrapping.
    /// </summary>
    private static List<string> WrapCellContent(string formattedCell, int maxWidth)
    {
        if (string.IsNullOrEmpty(formattedCell) || maxWidth <= 0)
            return new List<string> { "" };

        string plain = Markup.Remove(formattedCell);
        int totalWidth = CharacterWidth.GetDisplayWidth(plain);

        if (totalWidth <= maxWidth)
            return new List<string> { formattedCell };

        // Detect uniform formatting for reapplication on each wrapped line
        ExtractUniformMarkup(formattedCell, out string wrapPrefix, out string wrapSuffix);

        var lines = new List<string>();
        int pos = 0;

        while (pos < plain.Length)
        {
            // Find the character-wrap boundary first
            int lineWidth = 0;
            int endPos = pos;

            while (endPos < plain.Length)
            {
                int cw = CharacterWidth.GetDisplayWidth(plain[endPos]);
                if (lineWidth + cw > maxWidth && lineWidth > 0)
                    break;
                lineWidth += cw;
                endPos++;
            }

            if (endPos == pos) endPos = pos + 1; // At least one character

            // Try word-wrap: search backwards from the character boundary
            // for the last space.
            int actualEnd = endPos;
            if (endPos < plain.Length)
            {
                for (int i = endPos - 1; i > pos; i--)
                {
                    if (plain[i] == ' ')
                    {
                        actualEnd = i;
                        break;
                    }
                }
            }
            string segment = plain.Substring(pos, actualEnd - pos);
            if (segment.Length > 0)
                lines.Add(wrapPrefix + SpectreInlineRenderer.EscapeBrackets(segment) + wrapSuffix);

            // If we found a word break before the end, skip the space
            pos = actualEnd < endPos ? actualEnd + 1 : endPos;
        }

        return lines;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Row rendering
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders a logical table row (header or body) into one or more physical
    /// lines by wrapping cell content that exceeds its column width.
    /// Returns a list of Spectre-markup strings, one per physical line.
    /// </summary>
    private static List<string> RenderContentRowLines(List<string> formattedCells, int[] colWidths, List<TableAlignment> alignments, bool isHeader)
    {
        // Wrap each cell's content to fit its column
        var wrappedCells = new List<List<string>>();
        int maxLines = 0;

        for (int c = 0; c < colWidths.Length; c++)
        {
            string cellContent = c < formattedCells.Count ? formattedCells[c] : "";
            var wrapped = WrapCellContent(cellContent, colWidths[c]);
            wrappedCells.Add(wrapped);
            if (wrapped.Count > maxLines) maxLines = wrapped.Count;
        }

        // Ensure at least one line
        if (maxLines == 0) maxLines = 1;

        var lines = new List<string>();

        for (int li = 0; li < maxLines; li++)
        {
            var sb = new StringBuilder();
            sb.Append($"[{TableEdgesMarkup}]│[/]");

            for (int c = 0; c < colWidths.Length; c++)
            {
                string cellLine = li < wrappedCells[c].Count ? wrappedCells[c][li] : "";
                int displayWidth = GetCellDisplayWidth(cellLine);
                string styledContent = isHeader ? $"[bold]{cellLine}[/]" : cellLine;
                int padding = colWidths[c] - displayWidth;

                sb.Append(' '); // Left padding

                if (padding > 0)
                {
                    if (c < alignments.Count && alignments[c] == TableAlignment.Right)
                    {
                        sb.Append(' ', padding);
                        sb.Append(styledContent);
                    }
                    else if (c < alignments.Count && alignments[c] == TableAlignment.Center)
                    {
                        int leftPad = padding / 2;
                        int rightPad = padding - leftPad;
                        sb.Append(' ', leftPad);
                        sb.Append(styledContent);
                        sb.Append(' ', rightPad);
                    }
                    else
                    {
                        sb.Append(styledContent);
                        sb.Append(' ', padding);
                    }
                }
                else
                {
                    sb.Append(styledContent);
                }

                sb.Append(' '); // Right padding
                sb.Append($"[{TableEdgesMarkup}]│[/]");
            }

            lines.Add(sb.ToString());
        }

        return lines;
    }
}

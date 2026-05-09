using System;
using Xunit;

namespace OpenClawPTT.Tests.Misc;

public class MarkdownToSpectreConverterTests
{
    // ── Null and empty ────────────────────────────────────────────────────────

    [Fact]
    public void Convert_Null_ThrowsArgumentNullException()
        => Assert.Throws<ArgumentNullException>(() => MarkdownToSpectreConverter.Convert(null!));

    [Fact]
    public void Convert_EmptyString_ReturnsEmpty()
        => Assert.Equal("", MarkdownToSpectreConverter.Convert(""));

    [Fact]
    public void Convert_WhitespaceOnly_ReturnsEmpty()
        => Assert.Equal("", MarkdownToSpectreConverter.Convert("   \n\t  "));

    // ── Headings ──────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_H1_YieldsBoldUnderline()
        => Assert.Equal("[bold underline]Hello[/]", MarkdownToSpectreConverter.Convert("# Hello"));

    [Fact]
    public void Convert_H2_YieldsBold()
        => Assert.Equal("[bold]Hello[/]", MarkdownToSpectreConverter.Convert("## Hello"));

    [Fact]
    public void Convert_H3_YieldsBoldDim()
        => Assert.Equal("[bold dim]Hello[/]", MarkdownToSpectreConverter.Convert("### Hello"));

    [Fact]
    public void Convert_H6_YieldsBoldDim()
        => Assert.Equal("[bold dim]Deep[/]", MarkdownToSpectreConverter.Convert("###### Deep"));

    [Fact]
    public void Convert_HeadingWithInlineFormatting_PreservesFormatting()
        => Assert.Equal("[bold underline][bold]Hello[/] World[/]", MarkdownToSpectreConverter.Convert("# **Hello** World"));

    // ── Bold ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_BoldStars_YieldsBold()
        => Assert.Equal("[bold]bold text[/]", MarkdownToSpectreConverter.Convert("**bold text**"));

    [Fact]
    public void Convert_BoldUnderscores_YieldsBold()
        => Assert.Equal("[bold]bold text[/]", MarkdownToSpectreConverter.Convert("__bold text__"));

    [Fact]
    public void Convert_Bold_MultipleOccurrences()
        => Assert.Equal("[bold]a[/] and [bold]b[/]", MarkdownToSpectreConverter.Convert("**a** and **b**"));

    // ── Italic ────────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_ItalicStars_YieldsItalic()
        => Assert.Equal("[italic]italic text[/]", MarkdownToSpectreConverter.Convert("*italic text*"));

    [Fact]
    public void Convert_ItalicUnderscores_YieldsItalic()
        => Assert.Equal("[italic]italic text[/]", MarkdownToSpectreConverter.Convert("_italic text_"));

    [Fact]
    public void Convert_ItalicUnderscores_SkipsSnakeCaseIdentifiers()
    {
        // snake_case_word should NOT be turned into italic because underscores
        // are surrounded by word characters.
        var result = MarkdownToSpectreConverter.Convert("snake_case_word");
        Assert.DoesNotContain("[italic]", result);
        Assert.Contains("snake_case_word", result);
    }

    [Fact]
    public void Convert_ItalicUnderscores_StandaloneUnderscore_YieldsItalic()
    {
        // A single underscore surrounded by spaces should become italic.
        var result = MarkdownToSpectreConverter.Convert("_italic_");
        Assert.Equal("[italic]italic[/]", result);
    }

    // ── Bold + Italic ─────────────────────────────────────────────────────────

    [Fact]
    public void Convert_BoldItalicStars_YieldsBoldItalic()
        => Assert.Equal("[bold italic]bold italic[/]", MarkdownToSpectreConverter.Convert("***bold italic***"));

    [Fact]
    public void Convert_BoldItalic_PrecendenceBeforeBoldAndItalic()
    {
        // ***a*** should not become [bold][italic]a[/][/] — it must be [bold italic]a[/]
        var result = MarkdownToSpectreConverter.Convert("***a***");
        Assert.Equal("[bold italic]a[/]", result);
        Assert.DoesNotContain("[bold][italic]", result);
    }

    // ── Strikethrough ─────────────────────────────────────────────────────────

    [Fact]
    public void Convert_Strikethrough_YieldsStrikethrough()
        => Assert.Equal("[strikethrough]deleted[/]", MarkdownToSpectreConverter.Convert("~~deleted~~"));

    // ── Inline code ───────────────────────────────────────────────────────────

    [Fact]
    public void Convert_InlineCode_YieldsBoldYellow()
        => Assert.Equal("[bold gray89 on darkblue]code[/]", MarkdownToSpectreConverter.Convert("`code`"));

    [Fact]
    public void Convert_InlineCode_MultipleBackticks()
        => Assert.Equal("[bold gray89 on darkblue]a[/] and [bold gray89 on darkblue]b[/]", MarkdownToSpectreConverter.Convert("`a` and `b`"));

    // ── Links ────────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_Link_YieldsLinkMarkup()
        => Assert.Equal("[link=https://example.com]Example[/]", MarkdownToSpectreConverter.Convert("[Example](https://example.com)"));

    [Fact]
    public void Convert_LinkWithFormatting_PatternInsideLabel()
    {
        var result = MarkdownToSpectreConverter.Convert("[**bold link**](http://x.com)");
        var expectedResult = "[bold link=http://x.com]bold link[/]";
        ValidateMarkup(expectedResult);
        Assert.Contains(expectedResult, result);
        ValidateMarkup(result);
    }

    // ── Blockquotes ───────────────────────────────────────────────────────────

    [Fact]
    public void Convert_Blockquote_YieldsItalicDim()
        => Assert.Equal("[italic dim]quoted text[/]", MarkdownToSpectreConverter.Convert("> quoted text"));

    [Fact]
    public void Convert_Blockquote_NoSpaceAfterGt_YieldsItalicDim()
        => Assert.Equal("[italic dim]no space[/]", MarkdownToSpectreConverter.Convert(">no space"));

    [Fact]
    public void Convert_Blockquote_NestedInline_PreservesFormatting()
        => Assert.Equal("[italic dim][bold]bold[/] quote[/]", MarkdownToSpectreConverter.Convert("> **bold** quote"));

    // ── Thematic break ────────────────────────────────────────────────────────

    [Fact]
    public void Convert_HrDashes_YieldsDimLine()
        => Assert.Equal("[dim]────────────────────────────────────────[/]", MarkdownToSpectreConverter.Convert("---"));

    [Fact]
    public void Convert_HrStars_YieldsDimLine()
        => Assert.Equal("[dim]────────────────────────────────────────[/]", MarkdownToSpectreConverter.Convert("***"));

    [Fact]
    public void Convert_HrUnderscores_YieldsDimLine()
        => Assert.Equal("[dim]────────────────────────────────────────[/]", MarkdownToSpectreConverter.Convert("___"));

    [Fact]
    public void Convert_Hr_WithTrailingWhitespace_YieldsDimLine()
        => Assert.Equal("[dim]────────────────────────────────────────[/]", MarkdownToSpectreConverter.Convert("---   "));

    // ── Fenced code blocks ───────────────────────────────────────────────────

    [Fact]
    public void Convert_FencedCodeBlock_ProducesDecoratedBlock()
    {
        var md = "```\nlet x = 1;\n```";
        var result = MarkdownToSpectreConverter.Convert(md).Replace("\r\n", "\n");
        Assert.Contains("[italic]code[/]", result);
        Assert.Contains("[default on gray15]let x = 1;[/]", result);
        ValidateMarkup(result);
    }

    [Fact]
    public void Convert_FencedCodeBlockWithLanguage_ShowsCodeContent()
    {
        var md = "```javascript\nconsole.log('hi');\n```";
        var result = MarkdownToSpectreConverter.Convert(md).Replace("\r\n", "\n");
        // Language is not shown in current converter, only code content
        Assert.Contains("[default on gray15]console.log('hi');[/]", result);
        Assert.Contains("[italic]code[/]", result);
        ValidateMarkup(result);
    }

    [Fact]
    public void Convert_FencedCodeBlock_EmptyBlock_StillProducesFrame()
    {
        var md = "```\n```";
        var result = MarkdownToSpectreConverter.Convert(md);
        // Empty block still produces the frame lines (header + footer)
        Assert.Contains("[italic]code[/]", result);
        Assert.Contains("[dim]─", result);
        ValidateMarkup(result);
    }

    // ── Bracket escaping ─────────────────────────────────────────────────────

    [Fact]
    public void Convert_LiteralBrackets_EscapedAsDoubleBrackets()
    {
        // Brackets NOT part of a link should be escaped so Spectre treats them as literals.
        var result = MarkdownToSpectreConverter.Convert("[not a link]");
        Assert.Contains("[[not a link]]", result);
        ValidateMarkup(result);
    }

    [Fact]
    public void Convert_BracketsInLink_NotEscaped()
    {
        // Brackets inside a link URL should NOT be double-escaped.
        var result = MarkdownToSpectreConverter.Convert("[Example](https://example.com/path)");
        Assert.DoesNotContain("[[", result);
        Assert.Contains("[link=https://example.com/path]Example[/]", result);
        ValidateMarkup(result);
    }

    // ── Mixed / compound ─────────────────────────────────────────────────────

    [Fact]
    public void Convert_CompoundMarkdown_AllConstructs()
    {
        var md = "# Title\n\n**bold** and *italic*\n\n> blockquote\n\n---\n\n`code`\n\n[link](http://x.com)";
        var result = MarkdownToSpectreConverter.Convert(md);
        Assert.Contains("[bold underline]Title[/]", result);
        Assert.Contains("[bold]bold[/]", result);
        Assert.Contains("[italic]italic[/]", result);
        Assert.Contains("[italic dim]blockquote[/]", result);
        Assert.Contains("[dim]────────────────────────────────────────[/]", result);
        Assert.Contains("[bold gray89 on darkblue]code[/]", result);
        Assert.Contains("[link=http://x.com]link[/]", result);
        ValidateMarkup(result);
    }

    [Fact]
    public void Convert_MultilineParagraph_OneSpectreLinePerMarkdownLine()
    {
        var md = "line one\nline two\nline three";
        var result = MarkdownToSpectreConverter.Convert(md);
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        ValidateMarkup(result);
    }

    // ── CRLF handling ───────────────────────────────────────────────────────

    [Fact]
    public void Convert_CrlfLineEndings_SplitsCorrectly()
    {
        // Converter normalises CRLF to LF for splitting, then AppendLine()
        // uses Environment.Newline (\r\n on Windows) in output.
        var md = "line1\r\nline2";
        var result = MarkdownToSpectreConverter.Convert(md);
        // Input was split into 2 lines, not treated as one.
        Assert.Contains("line1", result);
        Assert.Contains("line2", result);
        ValidateMarkup(result);
    }

    // ── Tables ────────────────────────────────────────────────────────────────

    [Fact]
    public void Convert_Table_BasicTable_RendersWithBoxDrawing()
    {
        var md = "| a | b |\n|---|---|\n| 1 | 2 |";
        var result = MarkdownToSpectreConverter.Convert(md).Replace("\r\n", "\n");
        // Should contain box-drawing characters
        Assert.Contains("╭", result);
        Assert.Contains("╮", result);
        Assert.Contains("╰", result);
        Assert.Contains("╯", result);
        Assert.Contains("├", result);
        Assert.Contains("┤", result);
        Assert.Contains("│", result);
        // Header should be bold
        Assert.Contains("[bold]a[/]", result);
        Assert.Contains("[bold]b[/]", result);
        // Cells should appear
        Assert.Contains(" 1 ", result);
        Assert.Contains(" 2 ", result);
        ValidateMarkup(result);
    }

    [Fact]
    public void Convert_Table_WithInlineFormatting_AppliesMarkupToCells()
    {
        var md = "| **Name** | `code` |\n|----------|--------|\n| **bold** | `inline` |";
        var result = MarkdownToSpectreConverter.Convert(md).Replace("\r\n", "\n");
        Assert.Contains("[bold]Name[/]", result);
        Assert.Contains("[bold gray89 on darkblue]code[/]", result);
        Assert.Contains("[bold]bold[/]", result);
        Assert.Contains("[bold gray89 on darkblue]inline[/]", result);
        Assert.Contains("╭", result);
        Assert.Contains("│", result);
        Assert.Contains("├", result);
        Assert.Contains("╰", result);
        ValidateMarkup(result);
    }

    [Fact]
    public void Convert_Table_WithAlignment_RendersProperly()
    {
        // Right-aligned column
        var md = "| Left | Center | Right |\n|:-----|:------:|------:|\n| a    |   b    |     c |";
        var result = MarkdownToSpectreConverter.Convert(md).Replace("\r\n", "\n");
        Assert.Contains("╭", result);
        Assert.Contains("│", result);
        Assert.Contains("├", result);
        Assert.Contains("╰", result);
        ValidateMarkup(result);
    }

    [Fact]
    public void Convert_Table_SingleColumn_RendersCorrectly()
    {
        var md = "| Header |\n|--------|\n| Value  |";
        var result = MarkdownToSpectreConverter.Convert(md).Replace("\r\n", "\n");
        Assert.Contains("╭", result);
        Assert.Contains("╮", result);
        Assert.Contains("╰", result);
        Assert.Contains("╯", result);
        Assert.Contains("[bold]Header[/]", result);
        Assert.Contains("Value", result);
        ValidateMarkup(result);
    }

    [Fact]
    public void Convert_Table_WithMultipleRows_RendersAllRows()
    {
        var md = "| Col1 | Col2 |\n|------|------|\n| A    | B    |\n| C    | D    |\n| E    | F    |";
        var result = MarkdownToSpectreConverter.Convert(md).Replace("\r\n", "\n");
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        // Expected: top border, header, separator, 3 body rows, bottom border = 7 lines
        Assert.Equal(7, lines.Length);
        ValidateMarkup(result);
    }

    [Fact]
    public void Convert_Table_AvailableWidth_TruncatesWhenTooWide()
    {
        // Create a very wide table that won't fit in 40 chars
        var md = "| VeryLongHeader | AnotherLongColumn | ThirdWideColumn |\n|----------------|-------------------|-----------------|\n| CellOne        | CellTwo           | CellThree       |";
        var result = MarkdownToSpectreConverter.Convert(md, availableWidth: 40).Replace("\r\n", "\n");
        // Should still produce valid table structure, just narrower
        Assert.Contains("╭", result);
        Assert.Contains("│", result);
        Assert.Contains("├", result);
        Assert.Contains("╰", result);
        // Total visible width of each line should be <= 40
        // Use Markup.Remove to strip Spectre tags before measuring
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            string plain = Spectre.Console.Markup.Remove(line);
            int visibleWidth = CharacterWidth.GetDisplayWidth(plain);
            Assert.True(visibleWidth <= 42,
                $"Line visible width {visibleWidth} > 42. Content: {line}");
        }
        ValidateMarkup(result);
    }

    [Fact]
    public void Convert_Table_WithMarkdownLinks_FormatsCorrectly()
    {
        var md = "| Name | URL |\n|------|-----|\n| Example | [Click](https://example.com) |";
        var result = MarkdownToSpectreConverter.Convert(md).Replace("\r\n", "\n");
        Assert.Contains("[link=https://example.com]Click[/]", result);
        Assert.Contains("╭", result);
        Assert.Contains("│", result);
        ValidateMarkup(result);
    }

    [Fact]
    public void Convert_Table_EmptyCellsRenderCorrectly()
    {
        var md = "| A | B |\n|---|---|\n|   | X |\n| Y |   |";
        var result = MarkdownToSpectreConverter.Convert(md).Replace("\r\n", "\n");
        Assert.Contains("╭", result);
        Assert.Contains("│", result);
        Assert.Contains("├", result);
        Assert.Contains("╰", result);
        Assert.Contains("X", result);
        Assert.Contains("Y", result);
        ValidateMarkup(result);
    }

    [Fact]
    public void Convert_Table_AllLinesHaveConsistentWidth()
    {
        // Table with headers shorter than content — ensures no cell
        // steps out of the column boundaries.
        var md = @"
| 1 | 2 |
|---|---|
| Longer content A | Longer content B |
| Even longer content X | Even longer content Y |
| The longest content of all | Still fairly long |
";
        var result = MarkdownToSpectreConverter.Convert(md).Replace("\r\n", "\n");
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        Assert.True(lines.Length >= 6, $"Expected at least 6 lines, got {lines.Length}");

        // Measure the visible width of each line (strip markup tags)
        var widths = lines.Select(l =>
        {
            string plain = Spectre.Console.Markup.Remove(l);
            return CharacterWidth.GetDisplayWidth(plain);
        }).ToArray();

        // All lines must have identical visible width — no line exceeds the borders
        int expectedWidth = widths[0];
        for (int i = 1; i < widths.Length; i++)
        {
            Assert.Equal(expectedWidth, widths[i]);
        }

        // Also verify: top border has correct structure
        Assert.Equal('╭', lines[0][0]);
        Assert.Equal('╮', lines[0][^1]);
        Assert.Contains("┬", lines[0]);
        // Bottom border
        Assert.Equal('╰', lines[^1][0]);
        Assert.Equal('╯', lines[^1][^1]);
        // Header row
        Assert.StartsWith("│", lines[1]);
        Assert.EndsWith("│", lines[1]);
        // Separator
        Assert.Equal('├', lines[2][0]);
        Assert.Equal('┤', lines[2][^1]);
        // Body rows
        for (int r = 3; r < lines.Length - 1; r++)
        {
            Assert.StartsWith("│", lines[r]);
            Assert.EndsWith("│", lines[r]);
        }

        ValidateMarkup(result);
    }

    [Fact]
    public void Convert_Image_ConvertedAsLinkDueToUnsupportedSyntax()
    {
        // Images are unsupported but share link syntax; they are converted
        // as links (with the ! prefix passed through literally).
        var md = "![alt](http://x.com/img.png)";
        var result = MarkdownToSpectreConverter.Convert(md);
        Assert.Contains("[link=http://x.com/img.png]alt[/]", result);
        ValidateMarkup(result);
    }

    [Fact]
    public void Convert_FileLink_ConvertedAsLinkDueToUnsupportedSyntax()
    {
        // Images are unsupported but share link syntax; they are converted
        // as links (with the ! prefix passed through literally).
        var md = "Done — reverted back to `(pttController, textSender, shellHost, cfg, _factory)`. The extra `null` gatewayservice parameter has been dropped.";
        var result = MarkdownToSpectreConverter.Convert(md);
        ValidateMarkup(result);
    }
    
    private static void ValidateMarkup(string text)
    {
        var validateResult = MarkupValidator.Validate(text);
        Assert.True(validateResult.IsValid, $"Invalid markup in message: '{text.Replace("\n", "\\n")}'\n{validateResult}");
    }
}
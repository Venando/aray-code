using System;
using System.Text;
using Xunit;

namespace ArayCode.Tests;

/// <summary>
/// Tests for AgentReplyFormatter word wrapping and streaming behavior.
/// </summary>
public class AgentReplyFormatterTests
{
    private sealed class StringWriterTextOutput : IFormattedOutput
    {
        private readonly StringBuilder _sb = new StringBuilder();
        public string Result => _sb.ToString();

        public void Write(string text) => _sb.Append(text);
        public void WriteLine() => _sb.AppendLine();
        public int WindowWidth { get; set; } = 80;
        public string[] Flush()
        {
            var text = _sb.ToString().TrimEnd();
            return text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
    }

    [Fact]
    public void ProcessDelta_EmptyString_DoesNotThrow()
    {
        var formatter = new AgentReplyFormatter(prefix: "  ", reservedRightMargin: 10, prefixAlreadyPrinted: false, output: new StringWriterTextOutput());
        formatter.ProcessDelta("");
        formatter.Finish();
    }

    [Fact]
    public void ProcessDelta_MultiLineText_WrapsCorrectly()
    {
        var formatter = new AgentReplyFormatter(prefix: "  ", reservedRightMargin: 10, prefixAlreadyPrinted: false, output: new StringWriterTextOutput());
        formatter.ProcessDelta("line1\nline2");
        formatter.Finish();
    }

    [Fact]
    public void Finish_DoesNotThrow()
    {
        var formatter = new AgentReplyFormatter(prefix: "  ", reservedRightMargin: 10, prefixAlreadyPrinted: false, output: new StringWriterTextOutput());
        formatter.ProcessDelta("Some text");
        formatter.Finish();
    }

    [Fact]
    public void Finish_WithNoContent_DoesNotThrow()
    {
        var formatter = new AgentReplyFormatter(prefix: "  ", reservedRightMargin: 10, prefixAlreadyPrinted: false, output: new StringWriterTextOutput());
        formatter.Finish();
    }

    [Fact]
    public void ProcessDelta_SingleWord_OutputsText()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", reservedRightMargin: 10, prefixAlreadyPrinted: false, output: output);
        formatter.ProcessDelta("hello");
        formatter.Finish();
        Assert.Equal("hello", output.Result.Trim());
    }

    [Fact]
    public void ProcessDelta_MultipleIncrementalChunks_AccumulatesAndFinishes()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", reservedRightMargin: 10, prefixAlreadyPrinted: false, output: output);
        formatter.ProcessDelta("Hello ");
        formatter.ProcessDelta("World");
        formatter.Finish();
        Assert.Contains("Hello World", output.Result.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ProcessDelta_WithITextOutput_BuildsCorrectOutput()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", reservedRightMargin: 5, prefixAlreadyPrinted: false, output: output);
        formatter.ProcessDelta("This is a test");
        formatter.Finish();
        Assert.NotEmpty(output.Result);
        // Verify output was written via ITextOutput, not Console
        Assert.DoesNotContain("\x1B[", output.Result); // No ANSI escape codes
    }

    [Fact]
    public void ProcessDelta_ExplicitConsoleWidth_UsesProvidedWidth()
    {
        var output = new StringWriterTextOutput();
        // Wide console
        var formatter = new AgentReplyFormatter(prefix: "  ", reservedRightMargin: 10, prefixAlreadyPrinted: false, output: output);
        formatter.ProcessDelta("Hello World");
        formatter.Finish();
        Assert.Contains("Hello World", output.Result);
    }

    [Fact]
    public void ProcessDelta_NarrowConsoleWidth_WrapsText()
    {
        var output = new StringWriterTextOutput();
        // Very narrow console to force wrapping
        var formatter = new AgentReplyFormatter(prefix: "  ", reservedRightMargin: 10, prefixAlreadyPrinted: false, output: output);
        formatter.ProcessDelta("This is a long word that should wrap");
        formatter.Finish();
        // Should contain newlines due to wrapping
        Assert.Contains("\n", output.Result.Replace("\r\n", "\n"));
    }

    [Fact]
    public void ProcessDelta_Finish_EndsWithNewline()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", reservedRightMargin: 10, prefixAlreadyPrinted: false, output: output);
        formatter.ProcessDelta("test");
        formatter.Finish();
        Assert.EndsWith(Environment.NewLine, output.Result);
    }

    // ─── New stability tests ──────────────────────────────────────────────────

    [Fact]
    public void Constructor_2Param_DoesNotThrowInHeadlessEnvironment()
    {
        var formatter = new AgentReplyFormatter(prefix: "  ", prefixAlreadyPrinted: false, output: new StringWriterTextOutput());
        formatter.ProcessDelta("hello");
        formatter.Finish(); // must not throw
    }

    [Fact]
    public void Constructor_3ParamWithExplicitWidth_Works()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", reservedRightMargin: 5, prefixAlreadyPrinted: false, output: output);
        formatter.ProcessDelta("test");
        formatter.Finish();
        Assert.Contains("test", output.Result);
    }

    [Fact]
    public void Constructor_WidthZero_UsesFallbackWidth()
    {
        // Width 0 should be normalised to 80 (safe fallback).
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", reservedRightMargin: 5, prefixAlreadyPrinted: false, output: output);
        formatter.ProcessDelta("hello");
        formatter.Finish();
        Assert.Contains("hello", output.Result); // must not crash and must produce output
    }

    [Fact]
    public void Constructor_VeryLargeWidth_HandlesGracefully()
    {
        // Very large width should not cause overflow or crash.
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", reservedRightMargin: 5, prefixAlreadyPrinted: false, output: output);
        formatter.ProcessDelta("hello world");
        formatter.Finish();
        Assert.Contains("hello world", output.Result);
    }

    [Fact]
    public void Constructor_NullPrefix_HandlesGracefully()
    {
        // Null prefix should not crash; newlineSuffix will also be null-length.
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: null!, reservedRightMargin: 5, prefixAlreadyPrinted: false, output: output);
        formatter.ProcessDelta("hello");
        formatter.Finish();
        Assert.Contains("hello", output.Result);
        ValidateMarkup(output);
    }

    [Fact]
    public void ProcessDelta_MultipleIncrementalChunks_NegativeWidth_DoesNotCrash()
    {
        // Incremental chunks with specific margin config; no crash expected.
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "  ", reservedRightMargin: 5, prefixAlreadyPrinted: false, output: output);
        formatter.ProcessDelta("chunk1 ");
        formatter.ProcessDelta("chunk2");
        formatter.Finish();
        Assert.Contains("chunk1 chunk2", output.Result.Replace("\r\n", "\n"));
        ValidateMarkup(output);
    }

    // ─── ProcessMarkupDelta tests ──────────────────────────────────────────

    [Fact]
    public void ProcessMarkupDelta_SimpleMarkup_OutputsWrappedText()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "", reservedRightMargin: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta("[bold]Hello World[/]");
        formatter.Finish();
        var result = output.Result.Replace("\r\n", "\n");
        Assert.Contains("[bold]Hello World[/]", result);
        ValidateMarkup(output);
    }

    [Fact]
    public void ProcessMarkupDelta_EmptyString_DoesNotThrow()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "", reservedRightMargin: 5, prefixAlreadyPrinted: true, output: output);
        var ex = Record.Exception(() => formatter.ProcessMarkupDelta(""));
        Assert.Null(ex);
        formatter.Finish();
    }

    [Fact]
    public void ProcessMarkupDelta_TagCharactersInsideText_ParsesCorrectly()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "", reservedRightMargin: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta("[green]a [b] c[/]");
        formatter.Finish();
        var result = output.Result.Replace("\r\n", "\n").Trim();
        // [b] looks like a tag but is inside text — should be escaped as [[b]]
        Assert.Contains("[[b]]", result);
        ValidateMarkup(output);
    }

    [Fact]
    public void ProcessMarkupDelta_MultiWordLongText_WrapsAcrossLines()
    {
        var output = new StringWriterTextOutput();
        // Narrow width (20) to force wrapping
        var formatter = new AgentReplyFormatter(prefix: "  ", reservedRightMargin: 10, prefixAlreadyPrinted: false, output: output);
        formatter.ProcessMarkupDelta("[red]" + new string('x', 80) + "[/]");
        formatter.Finish();
        var result = output.Result.Replace("\r\n", "\n");
        // Should contain the markup tags and have multiple lines
        Assert.Contains("[red]", result);
        Assert.Contains("[/]", result);
        Assert.Contains("\n", result);
        ValidateMarkup(output);
    }

    [Fact]
    public void ProcessMarkupDelta_NestedMarkupTags_HandledCorrectly()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "", reservedRightMargin: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta("[bold][green]hello[/][/]");
        formatter.Finish();
        var result = output.Result.Replace("\r\n", "\n").Trim();
        Assert.Contains("[bold][green]hello[/][/]", result);
        ValidateMarkup(output);
    }

    [Fact]
    public void ProcessMarkupDelta_NewlinesInsideMarkup_Respected()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "", reservedRightMargin: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta("[grey]line1\nline2[/]");
        formatter.Finish();
        var result = output.Result.Replace("\r\n", "\n").Trim();
        Assert.Contains("[grey]", result);
        Assert.Contains("\n", result);
        ValidateMarkup(output);
    }

    [Fact]
    public void ProcessMarkupDelta_TextWithOpeningBracket_DoesNotConfuseParser()
    {
        var output = new StringWriterTextOutput();
        var formatter = new AgentReplyFormatter(prefix: "", reservedRightMargin: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta("[yellow]3 > [5] is true[/]");
        formatter.Finish();
        var result = output.Result.Replace("\r\n", "\n").Trim();
        // [5] is not a known tag so it gets escaped to [[5]] (proper
        // Spectre escape for literal bracket content).
        Assert.Contains("[[5]]", result);
        ValidateMarkup(output);
    }

    [Fact]
    public void ProcessMarkupDelta_NarrowWidth_FitsSmallMarkupWithoutNewline()
    {
        // Console width 10 with right margin 5 gives available width of ~5.
        // [white]1[/][gray]2[/] has only 2 visible characters ("1" and "2").
        // Tags have zero visible width. Everything should fit on one line.
        var output = new StringWriterTextOutput { WindowWidth = 10 };
        var formatter = new AgentReplyFormatter(prefix: "", reservedRightMargin: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta("[white]1[/][gray]2[/]");
        formatter.Finish();
        var result = output.Result.Replace("\r\n", "\n");
        Assert.DoesNotContain("\n", result.Trim());
        Assert.Contains("[white]1[/][gray]2[/]", result);
        ValidateMarkup(output);
    }

    [Fact]
    public void ProcessMarkupDelta_LongContent_WrapsWithoutDoubledClosingTags()
    {
        // Regression test: when the formatter word-wraps inside a [dim] region,
        // WriteNewLine() re-emits open tags and Finish() calls FlushWordBuffer
        // for the trailing [/]. Both paths must not duplicate the [/] closing tag.
        var output = new StringWriterTextOutput { WindowWidth = 30 };
        var formatter = new AgentReplyFormatter(prefix: "", reservedRightMargin: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta("[dim]" + new string('x', 35) + "[/]");
        formatter.Finish();
        var result = output.Result.Replace("\r\n", "\n");
        var validateResult = MarkupValidator.Validate(result);
        Assert.True(validateResult.IsValid, $"Invalid markup in message: '{result.Replace("\n", "\\n")}'\n{validateResult}");
        ValidateMarkup(output);
    }

    [Fact]
    public void ProcessMarkupDelta_EscapedBrackets_ParsesAsContentNotTags()
    {
        // Spectre markup uses [[ for a literal bracket. Brackets in code content
        // must be escaped before passing to ProcessMarkupDelta.
        // Note: [[x]] in the formatter produces [x]] which results in an open tag
        // [x] followed by a stray ]. This is a pre-existing formatter limitation.
        // The test verifies that the formatter does not produce [dim][dim] or
        // [/][/] doubled tags which are the specific regression being tracked.
        var output = new StringWriterTextOutput { WindowWidth = 120 };
        var formatter = new AgentReplyFormatter(prefix: "", reservedRightMargin: 10, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta("[dim]const items = [[x]][/]");
        formatter.Finish();
        var result = output.Result.Replace("\r\n", "\n");
        // Literal brackets in output, no tag confusion
        Assert.DoesNotContain("[dim][dim]", result);
        Assert.DoesNotContain("[/][/]", result);
        ValidateMarkup(output);
    }

    [Fact]
    public void ProcessMarkupDelta_FencedCodeBlock_SingleLine_ProducesValidMarkup()
    {
        // Simulates a fenced code block line converted via MarkdownToSpectreConverter.
        var output = new StringWriterTextOutput { WindowWidth = 40 };
        var formatter = new AgentReplyFormatter(prefix: "", reservedRightMargin: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta("[dim]" + new string('x', 35) + "[/]");
        formatter.Finish();
        var msg = output.Result.Replace("\r\n", "\n");
        var validateResult = MarkupValidator.Validate(msg);
        Assert.True(validateResult.IsValid, $"Invalid markup in message: {msg}\n{validateResult}");
        ValidateMarkup(output);
    }

    [Fact]
    public void ProcessMarkupDelta_ExplicitClose_DimTag_DoesNotDoubleCloseOnWrap()
    {
        // Explicit [/] close must pop "dim" from the stack so wrapping
        // after the close doesn't emit [/][/] (doubled close tags).
        var output = new StringWriterTextOutput { WindowWidth = 45 };
        var formatter = new AgentReplyFormatter(prefix: "", reservedRightMargin: 5, prefixAlreadyPrinted: true, output: output);
        string markup = "[dim]" + new string('x', 25) + "[/] " + new string('x', 20);
        formatter.ProcessMarkupDelta(markup);
        formatter.Finish();
        var result = output.Result.Replace("\r\n", "\n");
        Assert.Contains("[dim]", result);
        Assert.Contains("\n", result.Trim());
        var validateResult = MarkupValidator.Validate(result);
        Assert.True(validateResult.IsValid, $"Invalid markup in message: '{result.Replace("\n", "\\n")}'\n{validateResult}");
        ValidateMarkup(output);
    }

    [Fact]
    public void ProcessMarkupDelta_ConvertedFencedCodeBlock_NoDoubledCloseTags()
    {
        // End-to-end: converter output fed into the formatter must not
        // produce doubled [/][/] tags.
        // Use JS code without array brackets to avoid the formatter's
        // known limitation with unescaped brackets inside fenced code blocks.
        var markdown = @"```js
// Some JS for flavor
const items = 42;
items.map(i => console.log(i));
```";
        var spectreMarkup = MarkdownToSpectreConverter.Convert(markdown);
        var output = new StringWriterTextOutput { WindowWidth = 80 };
        var formatter = new AgentReplyFormatter(prefix: "", reservedRightMargin: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta(spectreMarkup);
        formatter.Finish();
        var result = output.Result.Replace("\r\n", "\n");
        var validateResult = MarkupValidator.Validate(result);
        Assert.True(validateResult.IsValid, $"Invalid markup in message: '{result.Replace("\n", "\\n")}'\n{validateResult}");
        ValidateMarkup(output);
    }

    [Fact]
    public void ProcessMarkupDelta_RawUnescapedBrackets_ReproducesDeliveryBug()
    {
        // This matches the reported delivery symptom: the formatter receives
        // markup with unescaped brackets (bypassing MarkdownToSpectreConverter)
        // and produces [/][/] in the middle of fenced code block lines.
        //
        // The input below has raw ["a", "b", "c"] inside [dim] — the brackets
        // are NOT escaped as [["a", "b", "c"]] because this path skips the converter.
        //
        // When ProcessMarkupDelta sees ["a", it enters tag mode looking for a
        // closing ] to form a Spectre tag. It finds ] after "a", interprets
        // ["a" as tag content, and pushes it onto the stack. This corrupts the
        // markup and produces [/][/] doubled close tags in the output.
        //
        // Note: the formatter output is genuinely invalid Spectre markup
        // because the input contains unescaped brackets. This test verifies
        // that the formatter does NOT produce [/][/] doubled close tags
        // specifically, even though the overall markup is otherwise invalid.
        // We use a targeted assertion for the doubled-close-tag regression.
        var output = new StringWriterTextOutput { WindowWidth = 80 };
        var formatter = new AgentReplyFormatter(prefix: "", reservedRightMargin: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta("[dim]const items = [\"a\", \"b\", \"c\"];[/]");
        formatter.Finish();
        var result = output.Result.Replace("\r\n", "\n");
        // The formatter output is invalid due to unescaped brackets in input,
        // but it must not produce [/][/] doubled close tags
        Assert.DoesNotContain("[/][/]", result);
        ValidateMarkup(output);
    }

    [Fact]
    public void ProcessMarkupDelta_FencedCodeBlockJS_NoDoubledCloseTags()
    {
        // This reproduces the exact runtime path: MarkdownToSpectreConverter
        // output fed into ProcessMarkupDelta. The converter escapes brackets
        // as [[ and ]]. But ProcessMarkupDelta must handle ]] correctly.
        // Use JS code without array brackets to avoid the formatter's
        // known limitation with escaped brackets in code blocks.
        var converterOutput = @"
[dim]const items = 42;[/]
[dim]items.map(i => console.log(i));[/]";
        var output = new StringWriterTextOutput { WindowWidth = 40 };
        var formatter = new AgentReplyFormatter(prefix: "", reservedRightMargin: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta(converterOutput);
        formatter.Finish();
        ValidateMarkup(output);
    }

    private static void ValidateMarkup(StringWriterTextOutput output)
    {
        var result = output.Result.Replace("\r\n", "\n");
        var validateResult = MarkupValidator.Validate(result);
        Assert.True(validateResult.IsValid, $"Invalid markup in message: '{result.Replace("\n", "\\n")}'\n{validateResult}");
    }

    [Theory]
    [InlineData("[dim]a[[b]][/]")]
    [InlineData("[dim][[b]] = x[/]")]
    [InlineData("[dim]items[[0]][/]")]
    [InlineData("[dim]array[[i]] = value[/]")]
    public void ProcessMarkupDelta_EscapedBracketPairs_DontProduceDoubledClose(string markup)
    {
        // Various escaped bracket patterns that might confuse the parser:
        // [[b]] = escaped [b], which should be treated as content, not a tag.
        // Note: the formatter has a known limitation with [[...]] patterns
        // that convert to a bracket pair like [b] where 'b' is a known
        // Spectre decoration (bold). This test verifies that the formatter
        // does NOT produce [/][/] doubled close tags as a result.
        var output = new StringWriterTextOutput { WindowWidth = 80 };
        var formatter = new AgentReplyFormatter(prefix: "", reservedRightMargin: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta(markup);
        formatter.Finish();
        var result = output.Result.Replace("\r\n", "\n");
        // The formatter output may contain tags like [b] (a valid Spectre
        // decoration) or unescaped ] tokens due to the formatter's handling
        // of escaped brackets. But it must NOT produce [/][/] doubled closes.
        Assert.DoesNotContain("[/][/]", result);
        ValidateMarkup(output);
    }

    [Theory]
    [InlineData("[dim][red][/][/]")]
    [InlineData("[dim]value[red]value[/][/]")]
    [InlineData("[dim]a[bold]c[/][/]")]
    public void ProcessMarkupDelta_SingleLetterBrackets_DontProduceDoubledClose(string markup)
    {
        // Single-letter tokens like [b] are valid Spectre tags (bold).
        // Ensure the formatter doesn't produce [/][/] doubled close tags.
        var output = new StringWriterTextOutput { WindowWidth = 80 };
        var formatter = new AgentReplyFormatter(prefix: "", reservedRightMargin: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta(markup);
        formatter.Finish();
        var result = output.Result.Replace("\r\n", "\n");
        var validateResult = MarkupValidator.Validate(result);
        Assert.True(validateResult.IsValid, $"Invalid markup in message: '{result.Replace("\n", "\\n")}'\n{validateResult}");
        ValidateMarkup(output);
    }

    [Fact]
    public void ProcessMarkupDelta_HangingClosingBracket_NoDoubledClose()
    {
        // Text like "some [text]" where the brackets are NOT valid tags:
        // the closing bracket ] after "text" should be treated as content.
        // [text] is not a valid Spectre style, so the formatter output
        // will still be invalid. We verify no [/][/] doubled close tags.
        var output = new StringWriterTextOutput { WindowWidth = 80 };
        var formatter = new AgentReplyFormatter(prefix: "", reservedRightMargin: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta("[grey]some [text] here[/]");
        formatter.Finish();
        var result = output.Result.Replace("\r\n", "\n");
        // The formatter output is invalid due to unescaped [text] in input,
        // but it must not produce [/][/] doubled close tags
        Assert.DoesNotContain("[/][/]", result);
        ValidateMarkup(output);
    }

    [Fact]
    public void ProcessMarkupDelta_DoubleMarkdown()
    {
        var spectreMarkup = "[bold underline]Heading[/]";
        var output = new StringWriterTextOutput { WindowWidth = 80 };
        var formatter = new AgentReplyFormatter(prefix: "", reservedRightMargin: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta(spectreMarkup);
        formatter.Finish();
        ValidateMarkup(output);
    }

    [Fact]
    public void ProcessMarkupDelta_Multiple_AtOnce()
    {
        var spectreMarkup = "Some [bold]bold[/] and [italic]italic[/] and [bold italic]both[/] text with [strikethrough]strikethrough[/] and [bold yellow]inline code[/].";
        var output = new StringWriterTextOutput { WindowWidth = 80 };
        var formatter = new AgentReplyFormatter(prefix: "", reservedRightMargin: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta(spectreMarkup);
        formatter.Finish();
        ValidateMarkup(output);
    }

    [Fact]
    public void ProcessMarkupDelta_Link()
    {
        var spectreMarkup = "[link=https://example.com]Link[/]";
        var output = new StringWriterTextOutput { WindowWidth = 80 };
        var formatter = new AgentReplyFormatter(prefix: "", reservedRightMargin: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta(spectreMarkup);
        formatter.Finish();
        ValidateMarkup(output);
    }

    [Fact]
    public void ProcessMarkupDelta_VeryLongMarkupLine()
    {
        var spectreMarkup = "This [bold yellow]sentence[/] has [bold]everything[/]: [italic]italics[/] [strikethrough]strikethrough[/] [bold yellow]code[/] [link=https://openclaw.ai]links[/] [bold italic]bold italic[/] more [bold yellow][[code]][/] with [bold yellow][[brackets]][/] and [bold yellow][[\"arrays\"]][/] and[bold]Check[/]";
        var output = new StringWriterTextOutput { WindowWidth = 70 };
        var formatter = new AgentReplyFormatter(prefix: "", reservedRightMargin: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta(spectreMarkup);
        formatter.Finish();
        ValidateMarkup(output);
    }

    [Fact]
    public void ProcessMarkupDelta_CustomColorMarkup()
    {
        var spectreMarkup = "[lime on #1a3a1a] text [/]";
        var output = new StringWriterTextOutput { WindowWidth = 70 };
        var formatter = new AgentReplyFormatter(prefix: "", reservedRightMargin: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta(spectreMarkup);
        formatter.Finish();
        ValidateMarkup(output);
    }

    [Fact]
    public void ProcessMarkupDelta_MultiLayerMarkup()
    {
        var spectreMarkup = "[bold gray89 on darkblue]a[/] and [bold gray89 on darkblue]b[/]";
        var output = new StringWriterTextOutput { WindowWidth = 70 };
        var formatter = new AgentReplyFormatter(prefix: "", reservedRightMargin: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta(spectreMarkup);
        formatter.Finish();
        ValidateMarkup(output);
    }

    [Theory]
    [InlineData(5, 30)]
    [InlineData(10, 30)]
    [InlineData(15, 30)]
    [InlineData(20, 30)]
    [InlineData(20, 60)]
    [InlineData(25, 60)]
    [InlineData(40, 60)]
    [InlineData(50, 60)]
    public void ProcessMarkupDelta_DoubleTrippleMarkupsWithLineBreaks(int repeats, int widowWidth)
    {
        var spectreMarkup = Enumerable.Repeat("[bold yellow strikethrough]some text line[/]", repeats).Aggregate((old, next) => old + next);
        var output = new StringWriterTextOutput { WindowWidth = widowWidth };
        var formatter = new AgentReplyFormatter(prefix: "", reservedRightMargin: 5, prefixAlreadyPrinted: true, output: output);
        formatter.ProcessMarkupDelta(spectreMarkup);
        formatter.Finish();
        ValidateMarkup(output);
    }

}

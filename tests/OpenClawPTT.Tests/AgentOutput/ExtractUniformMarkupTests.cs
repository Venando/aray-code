using Xunit;

namespace OpenClawPTT.Tests.AgentOutput;

/// <summary>
/// Tests for <see cref="SpectreTableRenderer.ExtractUniformMarkup"/>.
/// </summary>
public class ExtractUniformMarkupTests
{
    [Fact]
    public void ExtractUniformMarkup_EscapedBrackets_ExtractsUniformPart()
    {
        string input = "[bold gray89 on darkblue][[decoding]] 2.1K chars[/]";
        SpectreTableRenderer.ExtractUniformMarkup(input, out string prefix, out string suffix);

        // Escaped brackets "[[decoding]]" are treated as literal text, so the
        // outer [bold]...[/] is correctly identified as uniform markup.
        Assert.Equal("[bold gray89 on darkblue]", prefix);
        Assert.Equal("[/]", suffix);
    }

    [Fact]
    public void ExtractUniformMarkup_SimpleUniformMarkup_ExtractsPrefixAndSuffix()
    {
        string input = "[bold]hello world[/]";
        SpectreTableRenderer.ExtractUniformMarkup(input, out string prefix, out string suffix);

        Assert.Equal("[bold]", prefix);
        Assert.Equal("[/]", suffix);
    }

    [Fact]
    public void ExtractUniformMarkup_MultipleOpeningTags_ExtractsAllPrefixes()
    {
        string input = "[bold][red]hello[/]";
        SpectreTableRenderer.ExtractUniformMarkup(input, out string prefix, out string suffix);

        Assert.Equal("[bold][red]", prefix);
        Assert.Equal("[/]", suffix);
    }

    [Fact]
    public void ExtractUniformMarkup_MultipleClosingTags_ExtractsAllSuffixes()
    {
        string input = "[bold][red]hello[/][/]";
        SpectreTableRenderer.ExtractUniformMarkup(input, out string prefix, out string suffix);

        Assert.Equal("[bold][red]", prefix);
        Assert.Equal("[/][/]", suffix);
    }

    [Fact]
    public void ExtractUniformMarkup_NoMarkup_ReturnsEmpty()
    {
        string input = "plain text";
        SpectreTableRenderer.ExtractUniformMarkup(input, out string prefix, out string suffix);

        Assert.Empty(prefix);
        Assert.Empty(suffix);
    }

    [Fact]
    public void ExtractUniformMarkup_EmptyString_ReturnsEmpty()
    {
        string input = "";
        SpectreTableRenderer.ExtractUniformMarkup(input, out string prefix, out string suffix);

        Assert.Empty(prefix);
        Assert.Empty(suffix);
    }

    [Fact]
    public void ExtractUniformMarkup_NullString_ReturnsEmpty()
    {
        string input = null!;
        SpectreTableRenderer.ExtractUniformMarkup(input, out string prefix, out string suffix);

        Assert.Empty(prefix);
        Assert.Empty(suffix);
    }

    [Fact]
    public void ExtractUniformMarkup_OpeningTagOnly_ReturnsEmpty()
    {
        string input = "[bold]hello world";
        SpectreTableRenderer.ExtractUniformMarkup(input, out string prefix, out string suffix);

        Assert.Empty(prefix);
        Assert.Empty(suffix);
    }

    [Fact]
    public void ExtractUniformMarkup_ClosingTagOnly_ReturnsEmpty()
    {
        string input = "hello world[/bold]";
        SpectreTableRenderer.ExtractUniformMarkup(input, out string prefix, out string suffix);

        Assert.Empty(prefix);
        Assert.Empty(suffix);
    }

    [Fact]
    public void ExtractUniformMarkup_NestedEscapedBrackets_DoesNotThrow()
    {
        string input = "[[[nested]]]";
        SpectreTableRenderer.ExtractUniformMarkup(input, out string prefix, out string suffix);

        // Should not throw even though parsing is ambiguous with escaped brackets
        Assert.NotNull(prefix);
        Assert.NotNull(suffix);
    }

    [Fact]
    public void ExtractUniformMarkup_PrefixConsumesEntireString_ReturnsEmpty()
    {
        // This tests the edge case where prefix parsing would consume everything
        // leaving nothing for content, which previously could cause Substring errors
        string input = "[/]";
        SpectreTableRenderer.ExtractUniformMarkup(input, out string prefix, out string suffix);

        Assert.Empty(prefix);
        Assert.Empty(suffix);
    }

    [Fact]
    public void ExtractUniformMarkup_PrefixOverlapsSuffix_ReturnsEmpty()
    {
        // Edge case: tags that consume so much content that prefixEnd >= suffixStart
        string input = "[bold]text[/bold]more text[/]";
        SpectreTableRenderer.ExtractUniformMarkup(input, out string prefix, out string suffix);

        // The inner content contains markup, so full plain != inner plain
        Assert.Empty(prefix);
        Assert.Empty(suffix);
    }

    [Fact]
    public void ExtractUniformMarkup_UnbalancedBrackets_DoesNotThrow()
    {
        string input = "[bold unclosed text";
        SpectreTableRenderer.ExtractUniformMarkup(input, out string prefix, out string suffix);

        Assert.Empty(prefix);
        Assert.Empty(suffix);
    }
}

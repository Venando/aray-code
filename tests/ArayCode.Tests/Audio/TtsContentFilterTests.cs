using ArayCode.Services;
using Xunit;

namespace ArayCode.Tests.Audio;

public class TtsContentFilterTests
{
    [Theory]
    [InlineData("Hello world", "Hello world")]
    [InlineData("**bold** text", "bold text")]
    [InlineData("*italic* text", "italic text")]
    [InlineData("`code` here", "code here")]
    [InlineData("Check out https://example.com", "Check out [link]")]
    public void SanitizeForTts_StripsMarkdown(string input, string expected)
    {
        var result = TtsContentFilter.SanitizeForTts(input);
        Assert.Contains(expected, result);
    }

    [Fact]
    public void SanitizeForTts_RemovesCodeBlocks()
    {
        var input = "```csharp\nvar x = 1;\n```";
        var result = TtsContentFilter.SanitizeForTts(input);
        // Smart mode: short block → [Short csharp snippet]
        Assert.Contains("Short", result);
        Assert.DoesNotContain("var x", result);
    }

    [Fact]
    public void SanitizeForTts_HandlesLinks()
    {
        var input = "See [docs](https://docs.example.com) for more";
        var result = TtsContentFilter.SanitizeForTts(input);
        Assert.Contains("docs", result);
        Assert.DoesNotContain("https://", result);
    }

    [Theory]
    [InlineData("```code```", true)]
    [InlineData("**bold**", true)]
    [InlineData("# header", true)]
    [InlineData("plain text", false)]
    public void HasSpecialFormatting_DetectsMarkdown(string input, bool expected)
    {
        var result = TtsContentFilter.HasSpecialFormatting(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("short", 10, "short")]
    [InlineData("this is longer", 10, "this i ...")]
    public void Truncate_RespectsMaxLength(string input, int maxLen, string expected)
    {
        var result = TtsContentFilter.Truncate(input, maxLen);
        Assert.Equal(expected, result);
        Assert.True(result.Length <= maxLen);
    }
}

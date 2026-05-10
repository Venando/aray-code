using Xunit;
using Spectre.Console;

namespace OpenClawPTT.Tests.AgentOutput;

/// <summary>
/// Diagnostic tests documenting Spectre.Console markup behavior.
/// These help understand how Markup.Remove and Markup.Escape handle
/// edge cases that affect table rendering.
/// </summary>
public class SpectreMarkupBehaviorTests
{
    // ── Markup.Remove ─────────────────────────────────────────────────────

    [Fact]
    public void MarkupRemove_OnFormattedCellWithEscapedBrackets()
    {
        string formatted = "[bold gray89 on darkblue]3.2K/8K ctx 40% [[decoding]][/]";
        string plain = Markup.Remove(formatted);
        Assert.Equal("3.2K/8K ctx 40% [decoding]", plain);
    }

    [Fact]
    public void MarkupRemove_OnInnerContentWithEscapedBrackets()
    {
        string inner = "3.2K/8K ctx 40% [[decoding]]";
        string plain = Markup.Remove(inner);
        Assert.Equal("3.2K/8K ctx 40% [decoding]", plain);
    }

    [Fact]
    public void MarkupRemove_OnFullStringWithDecodingAtStart()
    {
        string formatted = "[bold gray89 on darkblue][[decoding]] 2.1K chars[/]";
        string plain = Markup.Remove(formatted);
        Assert.Equal("[decoding] 2.1K chars", plain);
    }

    [Fact]
    public void MarkupRemove_OnUnknownTagInMiddle_ReturnsEmpty()
    {
        // [decoding] is treated as an unknown markup tag by Spectre.Console
        // Markup.Remove strips it entirely, leaving empty string
        string input = "[bold gray89 on darkblue][decoding][/]";
        string removed = Markup.Remove(input);
        Assert.Equal("", removed);
    }

    [Fact]
    public void MarkupRemove_OnUnknownTagWithContent_StripsTag()
    {
        string input = "[decoding]hello[/]";
        string removed = Markup.Remove(input);
        Assert.Equal("hello", removed);
    }

    [Fact]
    public void MarkupRemove_OnSelfContainedUnknownTag_Throws()
    {
        // A bare [decoding] without closing tag is malformed
        string input = "[decoding]";
        Assert.Throws<InvalidOperationException>(() => Markup.Remove(input));
    }

    // ── Markup.Escape ──────────────────────────────────────────────────────

    [Fact]
    public void MarkupEscape_EscapesAlreadyEscapedBrackets()
    {
        // Markup.Escape doubles ALL brackets, even already-escaped ones
        string input = "3.2K/8K ctx 40% [[decoding]]";
        string escaped = Markup.Escape(input);
        Assert.Equal("3.2K/8K ctx 40% [[[[decoding]]]]", escaped);
    }

    // ── CharacterWidth ───────────────────────────────────────────────────

    [Fact]
    public void CharacterWidth_OfDecodingString()
    {
        string plain = "3.2K/8K ctx 40% [decoding]";
        int width = CharacterWidth.GetDisplayWidth(plain);
        Assert.Equal(plain.Length, width); // All ASCII
    }
}

using Xunit;
using Spectre.Console;
using OpenClawPTT;
using System.Collections.Generic;
using System.Reflection;

namespace OpenClawPTT.Tests.AgentOutput;

public class DirectLlmGpt4oDiagnosticTests
{
    [Fact]
    public void MarkupRemove_OnDirectLlmCell()
    {
        string formatted = "[bold gray89 on darkblue]Direct LLM: GPT-4o[/]";
        string plain = Markup.Remove(formatted);
        Assert.Equal("Direct LLM: GPT-4o", plain);
        Assert.Equal(18, plain.Length);
    }

    [Fact]
    public void CharacterWidth_OnDirectLlmCell()
    {
        string plain = "Direct LLM: GPT-4o";
        int width = CharacterWidth.GetDisplayWidth(plain);
        Assert.Equal(18, width);
    }

    [Fact]
    public void GetCellDisplayWidth_OnDirectLlmCell()
    {
        string formatted = "[bold gray89 on darkblue]Direct LLM: GPT-4o[/]";
        string plain = Markup.Remove(formatted);
        int width = CharacterWidth.GetDisplayWidth(plain);
        Assert.Equal(18, width);
    }

    [Fact]
    public void SpectreMarkupValidator_OnBoldGray89()
    {
        var result = SpectreMarkupValidator.ValidateTagContent("bold gray89 on darkblue");
        Assert.True(result.IsValid);
        Assert.False(result.ShouldEscape);
    }

    [Fact]
    public void ExtractUniformMarkup_OnDirectLlmCell()
    {
        string input = "[bold gray89 on darkblue]Direct LLM: GPT-4o[/]";
        SpectreTableRenderer.ExtractUniformMarkup(input, out string prefix, out string suffix);
        Assert.Equal("[bold gray89 on darkblue]", prefix);
        Assert.Equal("[/]", suffix);
    }

    [Fact]
    public void WrapCellContent_DirectLlmCell_DoesNotWrap()
    {
        var method = typeof(SpectreTableRenderer).GetMethod("WrapCellContent", 
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        
        var result = method.Invoke(null, new object[] { "[bold gray89 on darkblue]Direct LLM: GPT-4o[/]", 28 }) as List<string>;
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("[bold gray89 on darkblue]Direct LLM: GPT-4o[/]", result[0]);
    }

    [Fact]
    public void CharacterWidth_WhiteCircleEmoji()
    {
        Assert.Equal(2, CharacterWidth.GetDisplayWidth("⚪"[0]));
    }

    [Fact]
    public void CharacterWidth_GreenCircleEmoji()
    {
        // 🟢 is a surrogate pair, GetDisplayWidth(string) iterates chars
        Assert.Equal(2, CharacterWidth.GetDisplayWidth("🟢"));
    }

    [Fact]
    public void CharacterWidth_RedCircleEmoji()
    {
        Assert.Equal(2, CharacterWidth.GetDisplayWidth("🔴"));
    }

    [Fact]
    public void CharacterWidth_YellowCircleEmoji()
    {
        Assert.Equal(2, CharacterWidth.GetDisplayWidth("🟡"));
    }

    [Fact]
    public void CharacterWidth_BlackCircle()
    {
        Assert.Equal(2, CharacterWidth.GetDisplayWidth('●'));
    }

    [Fact]
    public void CharacterWidth_OfflineModeCell()
    {
        string plain = "⚪ Offline Mode";
        int width = CharacterWidth.GetDisplayWidth(plain);
        Assert.Equal(15, width); // ⚪=2 + space=1 + Offline=7 + space=1 + Mode=4 = 15
    }

    [Fact]
    public void CharacterWidth_ClaudeSonnetCell()
    {
        string plain = "🟢 Claude Sonnet";
        int width = CharacterWidth.GetDisplayWidth(plain);
        Assert.Equal(16, width); // 🟢=2 + space=1 + Claude=6 + space=1 + Sonnet=6 = 16
    }

    [Fact]
    public void CharacterWidth_ClaudeSonnetWithDot()
    {
        string plain = "🟢 Claude Sonnet ●";
        int width = CharacterWidth.GetDisplayWidth(plain);
        Assert.Equal(19, width); // 🟢=2 + space=1 + Claude=6 + space=1 + Sonnet=6 + space=1 + ●=2 = 19
    }
}

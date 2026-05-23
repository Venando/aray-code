using Moq;
using ArayCode.Services;
using Xunit;

namespace ArayCode.Tests.Audio;

public class TtsSummarizerTests
{
    [Fact]
    public async Task SummarizeForTtsAsync_UsesDirectLlm()
    {
        var mockLlm = new Mock<IDirectLlmService>();
        mockLlm.Setup(x => x.IsConfigured).Returns(true);
        mockLlm.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync("Summary here");

        var summarizer = new TtsSummarizer(mockLlm.Object);
        var config = new AppConfig
        {
            TtsMaxChars = 500,
            TtsCodeBlockMode = "smart"
        };

        var result = await summarizer.SummarizeForTtsAsync("Long text here", config);

        Assert.Equal("Summary here", result);
        mockLlm.Verify(x => x.SendAsync(It.Is<string>(s => s.Contains("Summarize")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SummarizeForTtsAsync_ThrowsWhenNotConfigured()
    {
        var mockLlm = new Mock<IDirectLlmService>();
        mockLlm.Setup(x => x.IsConfigured).Returns(false);

        var summarizer = new TtsSummarizer(mockLlm.Object);
        var config = new AppConfig();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => summarizer.SummarizeForTtsAsync("text", config));
    }

    [Fact]
    public async Task SummarizeForTtsAsync_ThrowsWhenNullLlm()
    {
        var summarizer = new TtsSummarizer(null);
        var config = new AppConfig();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => summarizer.SummarizeForTtsAsync("text", config));
    }

    [Fact]
    public async Task SummarizeForTtsAsync_PreprocessesMarkdownBeforeSendingToLlm()
    {
        // Arrange: mock LLM receives the prompt
        string? capturedPrompt = null;
        var mockLlm = new Mock<IDirectLlmService>();
        mockLlm.Setup(x => x.IsConfigured).Returns(true);
        mockLlm.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Callback<string, CancellationToken>((prompt, _) => capturedPrompt = prompt)
               .ReturnsAsync("Summarized");

        var summarizer = new TtsSummarizer(mockLlm.Object);
        var config = new AppConfig
        {
            TtsMaxChars = 500,
            TtsCodeBlockMode = "skip"
        };

        // Act: send text with markdown and URLs
        await summarizer.SummarizeForTtsAsync("See https://example.com and `code` here", config);

        // Assert: the prompt sent to LLM has markdown stripped and URLs replaced
        Assert.NotNull(capturedPrompt);
        Assert.DoesNotContain("https://", capturedPrompt);
        Assert.DoesNotContain("`code`", capturedPrompt);
        Assert.Contains("code", capturedPrompt);
        Assert.Contains("[link]", capturedPrompt);
    }

    [Fact]
    public async Task SummarizeForTtsAsync_PreprocessesCodeBlocksBeforeSendingToLlm()
    {
        string? capturedPrompt = null;
        var mockLlm = new Mock<IDirectLlmService>();
        mockLlm.Setup(x => x.IsConfigured).Returns(true);
        mockLlm.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Callback<string, CancellationToken>((prompt, _) => capturedPrompt = prompt)
               .ReturnsAsync("Summarized");

        var summarizer = new TtsSummarizer(mockLlm.Object);
        var config = new AppConfig
        {
            TtsMaxChars = 500,
            TtsCodeBlockMode = "skip"
        };

        // Act: send text with a code block
        await summarizer.SummarizeForTtsAsync("```python\nprint('hello')\n```", config);

        // Assert: code block is replaced (Smart mode preprocesses regardless of prompt mode)
        Assert.NotNull(capturedPrompt);
        Assert.Contains("Short", capturedPrompt);
        Assert.DoesNotContain("print", capturedPrompt);
    }
}

using Moq;
using OpenClawPTT.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OpenClawPTT.Tests.Input;

public class InputHandlerTests
{
    [Fact]
    public async Task SendTextAsync_CallsTextSender()
    {
        var mockSender = new Mock<ITextMessageSender>();
        var handler = new InputHandler(mockSender.Object);

        await handler.SendTextAsync("Hello world", CancellationToken.None);

        mockSender.Verify(x => x.SendAsync("Hello world", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendTextAsync_EmptyText_DoesNotCallSender()
    {
        var mockSender = new Mock<ITextMessageSender>();
        var handler = new InputHandler(mockSender.Object);

        await handler.SendTextAsync("", CancellationToken.None);
        await handler.SendTextAsync("   ", CancellationToken.None);

        mockSender.Verify(x => x.SendAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendTextAsync_WhitespaceText_TrimsAndSends()
    {
        var mockSender = new Mock<ITextMessageSender>();
        var handler = new InputHandler(mockSender.Object);

        await handler.SendTextAsync("  Hello world  ", CancellationToken.None);

        mockSender.Verify(x => x.SendAsync("Hello world", It.IsAny<CancellationToken>()), Times.Once);
    }
}

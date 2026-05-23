using System;
using System.Threading;
using System.Threading.Tasks;
using ArayCode.Services;
using Xunit;

namespace ArayCode.Tests;

/// <summary>
/// Stability tests for InputHandler.
/// InputHandler is now a simplified facade that no longer polls console keys.
/// Non-command text is sent via StreamShell's UserInputSubmitted events (StreamShellInputHandler).
/// HandleInputAsync is a no-op; SendTextAsync sends messages directly.
/// </summary>
public class InputHandlerStabilityTests
{
    /// <summary>
    /// Test double for ITextMessageSender that records calls and optionally throws.
    /// </summary>
    private sealed class RecordingTextSender : ITextMessageSender
    {
        public readonly List<string> SentMessages = new();
        public Exception? ExceptionToThrow;
        public bool ThrowOnSend;

        public Task SendAsync(string text, CancellationToken ct, bool printMessage = true)
        {
            ct.ThrowIfCancellationRequested();
            if (ThrowOnSend && ExceptionToThrow != null)
                throw ExceptionToThrow;
            SentMessages.Add(text);
            return Task.CompletedTask;
        }
    }

    private readonly RecordingTextSender _sender = new();
    private readonly InputHandler _handler;

    public InputHandlerStabilityTests()
    {
        _handler = new InputHandler(_sender);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HandleInputAsync — no-op, always returns Continue
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HandleInputAsync_AlwaysReturnsContinue()
    {
        var result = await _handler.HandleInputAsync(CancellationToken.None);
        Assert.Equal(InputResult.Continue, result);
    }

    [Fact]
    public async Task HandleInputAsync_WithCancelledToken_ReturnsContinue()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _handler.HandleInputAsync(cts.Token);

        Assert.Equal(InputResult.Continue, result);
    }

    [Fact]
    public async Task HandleInputAsync_DoesNotSendAnyMessages()
    {
        await _handler.HandleInputAsync(CancellationToken.None);
        Assert.Empty(_sender.SentMessages);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SendTextAsync — sends messages via ITextMessageSender
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SendTextAsync_SendsMessage()
    {
        await _handler.SendTextAsync("hello from test", CancellationToken.None);

        Assert.Contains("hello from test", _sender.SentMessages);
    }

    [Fact]
    public async Task SendTextAsync_EmptyText_DoesNotSend()
    {
        await _handler.SendTextAsync("", CancellationToken.None);
        Assert.Empty(_sender.SentMessages);
    }

    [Fact]
    public async Task SendTextAsync_WhitespaceText_DoesNotSend()
    {
        await _handler.SendTextAsync("   \t  ", CancellationToken.None);
        Assert.Empty(_sender.SentMessages);
    }

    [Fact]
    public async Task SendTextAsync_NullText_DoesNotSend()
    {
        await _handler.SendTextAsync(null!, CancellationToken.None);
        Assert.Empty(_sender.SentMessages);
    }

    [Fact]
    public async Task SendTextAsync_LongText_SendsAsIs()
    {
        var longText = new string('x', 10_000);
        await _handler.SendTextAsync(longText, CancellationToken.None);

        Assert.Single(_sender.SentMessages);
        Assert.Equal(10_000, _sender.SentMessages[0].Length);
    }

    [Fact]
    public async Task SendTextAsync_SenderThrows_PropagatesException()
    {
        _sender.ThrowOnSend = true;
        _sender.ExceptionToThrow = new InvalidOperationException("Network error");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _handler.SendTextAsync("hello", CancellationToken.None));
    }

    [Fact]
    public async Task SendTextAsync_CancelledToken_Throws()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _handler.SendTextAsync("hello", cts.Token));
    }
}

namespace ArayCode.Services;

/// <summary>Unified text message sending to the gateway.</summary>
public interface ITextMessageSender
{
    Task SendAsync(string text, CancellationToken ct, bool printMessage = true);
}

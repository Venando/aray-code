namespace ArayCode.Services;

/// <summary>
/// Decorator around <see cref="ITextMessageSender"/> that notifies the
/// <see cref="IConversationNamingService"/> when a message is sent.
/// This allows automatic conversation naming based on the first user message.
/// </summary>
public sealed class ConversationNamingTextMessageSender : ITextMessageSender
{
    private readonly ITextMessageSender _inner;
    private readonly IConversationNamingService _namingService;

    public ConversationNamingTextMessageSender(ITextMessageSender inner, IConversationNamingService namingService)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _namingService = namingService ?? throw new ArgumentNullException(nameof(namingService));
    }

    public async Task SendAsync(string text, CancellationToken ct, bool printMessage = true)
    {
        _namingService.OnMessageSent(text);
        await _inner.SendAsync(text, ct, printMessage);
    }
}

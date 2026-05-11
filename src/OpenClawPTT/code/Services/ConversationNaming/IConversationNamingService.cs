namespace OpenClawPTT.Services;

/// <summary>
/// Generates and tracks descriptive names for conversations based on the first user message.
/// Uses Direct LLM (if configured) to summarize the conversation start into a short name.
/// </summary>
public interface IConversationNamingService : IDisposable
{
    /// <summary>
    /// Gets the conversation name for the currently active session, or null if not yet generated.
    /// </summary>
    string? GetCurrentConversationName();

    /// <summary>
    /// Called when a message is sent. If this is the first message for the current session,
    /// triggers async name generation via Direct LLM.
    /// </summary>
    void OnMessageSent(string messageText);

    /// <summary>
    /// Event raised when the conversation name changes (including when cleared on agent switch).
    /// </summary>
    event Action<string?>? ConversationNameChanged;
}

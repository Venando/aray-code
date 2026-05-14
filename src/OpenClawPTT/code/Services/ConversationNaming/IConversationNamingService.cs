using OpenClawPTT.Services.Commands;

namespace OpenClawPTT.Services;

/// <summary>
/// Generates and tracks descriptive names for conversations.
/// Uses multiple messages from both user and agent (including session history)
/// to produce adaptive conversation titles.
/// </summary>
public interface IConversationNamingService : IDisposable
{
    /// <summary>
    /// Gets the conversation name for the currently active session, or null if not yet generated.
    /// </summary>
    string? GetCurrentConversationName();

    /// <summary>
    /// Called when a user message is sent. Accumulates user messages and triggers
    /// adaptive naming as the conversation grows.
    /// </summary>
    void OnMessageSent(string messageText);

    /// <summary>
    /// Called when an agent reply is received. Accumulates agent replies and
    /// triggers adaptive naming as the conversation grows.
    /// </summary>
    void OnAgentReplyReceived(string replyText);

    /// <summary>
    /// Called when an agent reply final is received. Accumulates agent replies and
    /// triggers adaptive naming as the conversation grows.
    /// </summary>
    void OnAgentReplyFinalReceived(string replyText);

    /// <summary>
    /// Provides session history entries for the current session.
    /// These are used to build richer naming context (past messages before this session).
    /// </summary>
    void SetSessionHistory(List<ChatHistoryEntry>? history);

    /// <summary>
    /// Called when any command is executed. The service checks whether the command
    /// should reset the conversation name (e.g. /reset, /new).
    /// </summary>
    void OnCommandExecuted(object? sender, CommandExecutedEventArgs e);

    /// <summary>
    /// Event raised when the conversation name changes (including when cleared on agent switch).
    /// </summary>
    event Action<string?>? ConversationNameChanged;
}

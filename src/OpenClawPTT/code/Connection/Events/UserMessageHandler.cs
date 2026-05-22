using OpenClawPTT.Services;

namespace OpenClawPTT;

/// <summary>
/// Handles <see cref="UserMessageEvent"/> — user messages that arrived from the
/// gateway (potentially sent by another node).  Prints them unless they were
/// just sent from this node, in which case they are already on screen via
/// <see cref="TextMessageSender"/>.
/// </summary>
public class UserMessageHandler : IEventHandler<UserMessageEvent>
{
    private readonly IColorConsole _console;
    private readonly IRecentMessageTracker _tracker;

    public UserMessageHandler(IColorConsole console, IRecentMessageTracker tracker)
    {
        _console = console;
        _tracker = tracker;
    }

    public async Task HandleAsync(UserMessageEvent evt)
    {
        if (string.IsNullOrWhiteSpace(evt.ContentText))
            return;

        // If we sent this message locally, it was already printed by
        // TextMessageSender — skip to avoid double-printing.
        if (_tracker.WasRecentlySent(evt.ContentText))
            return;

        _console.PrintUserMessage(evt.ContentText);
    }
}

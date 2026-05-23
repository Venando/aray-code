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

    // Track recently printed incoming messages to deduplicate gateway echoes.
    // The gateway may send the same user message twice: once as raw text and
    // once as a rich content array (with a messageId). We keep a short window
    // (5 s) of seen messageIds and printed content hashes.
    // Content keys are normalised (trimmed, whitespace-collapsed, Unicode NFC)
    // to match gateway-echoed messages that differ in minor formatting.
    private readonly HashSet<string> _seenMessageIds = new();
    private readonly Dictionary<string, DateTimeOffset> _printedContent = new();
    private readonly TimeSpan _dedupWindow = TimeSpan.FromSeconds(5);
    private readonly object _dedupLock = new();

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

        var normalizedKey = GetNormalizedContent(evt.ContentText);

        lock (_dedupLock)
        {
            CleanupPrintedWindow();

            // Deduplicate by MessageId if present.
            if (!string.IsNullOrEmpty(evt.MessageId))
            {
                if (_seenMessageIds.Contains(evt.MessageId))
                    return;

                // If we already printed the same content anonymously (no MessageId)
                // within the window, skip this canonical version.
                if (_printedContent.TryGetValue(normalizedKey, out var ts)
                    && DateTimeOffset.UtcNow - ts <= _dedupWindow)
                {
                    _seenMessageIds.Add(evt.MessageId);
                    return;
                }

                _seenMessageIds.Add(evt.MessageId);
            }
            else
            {
                // No MessageId — deduplicate by content within the window.
                if (_printedContent.TryGetValue(normalizedKey, out var ts)
                    && DateTimeOffset.UtcNow - ts <= _dedupWindow)
                    return;
            }

            _printedContent[normalizedKey] = DateTimeOffset.UtcNow;
        }

        _console.PrintUserMessage(evt.ContentText);
    }

    /// <summary>
    /// Normalises message content using <see cref="RecentMessageTracker.Normalise"/>
    /// so that gateway-echoed messages that differ in whitespace or Unicode
    /// normalisation still match.
    /// </summary>
    private static string GetNormalizedContent(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return RecentMessageTracker.Normalise(text);
    }

    private void CleanupPrintedWindow()
    {
        var cutoff = DateTimeOffset.UtcNow - _dedupWindow;
        var stale = _printedContent
            .Where(kvp => kvp.Value < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (var key in stale)
            _printedContent.Remove(key);
    }
}

using System.Text.Json;

namespace OpenClawPTT.Services;

/// <summary>
/// Generates human-readable one-line descriptions of the last agent activity.
/// Encapsulates the description logic that was previously embedded in <see cref="AgentActivityStore"/>.
/// </summary>
public sealed class AgentActivityDescriber
{
    private readonly IAgentActivityStore _store;
    private readonly AgentActivityFormatter _formatter;

    public AgentActivityDescriber(IAgentActivityStore store, AgentActivityFormatter? formatter = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _formatter = formatter ?? AgentActivityFormatter.Default;
    }

    public string? GetLastActionDescription(string sessionKey)
    {
        // Query all sources by timestamp: history + live events
        var lastHist = _store.GetLastHistoryMessage(sessionKey);
        var lastTool = _store.GetLastToolCall(sessionKey);
        var lastMsg = _store.GetLastAssistantMessage(sessionKey);
        var lastUser = _store.GetLastUserMessage(sessionKey);

        long? histTime = lastHist?.Timestamp;
        long? toolTime = lastTool?.Ts;
        long? msgTime = lastMsg?.Timestamp;
        long? userTime = lastUser?.Timestamp;

        // History message is most recent
        if (histTime is { } ht && (toolTime is null || ht >= toolTime)
            && (msgTime is null || ht >= msgTime) && (userTime is null || ht >= userTime))
        {
            return FormatHistoryAction(lastHist!);
        }

        // Tool call
        if (toolTime is { } tt && (msgTime is null || tt >= msgTime) && (userTime is null || tt >= userTime))
            return _formatter.FormatTool(lastTool!.ToolName, lastTool.ArgsJson);

        // Assistant message
        if (msgTime is { } mt && (userTime is null || mt >= userTime))
            return _formatter.FormatAssistantMessage(null);

        // User message
        if (userTime is not null && lastUser?.ContentText is { } ct)
            return _formatter.FormatUserMessage(ct);

        return null;
    }

    private string? FormatHistoryAction(HistoryMessageEvent e)
    {
        // Tool calls in the history message
        if (e.ToolCalls.Count > 0)
        {
            var lastTc = e.ToolCalls[^1];
            return _formatter.FormatTool(lastTc.Name, lastTc.ArgumentsJson);
        }

        // Assistant text
        if (e.Role == "assistant")
            return _formatter.FormatAssistantMessage(e.ContentText);

        // User text — skip, we don't show user's own messages as "last action"
        if (e.Role == "user" && !string.IsNullOrWhiteSpace(e.ContentText))
            return null;

        return null;
    }
}

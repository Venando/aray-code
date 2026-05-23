using System.Text.Json;

namespace ArayCode.Services;

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

    public string? GetLastActionDescription(string sessionKey) =>
        _store.SelectLatestActivity(
            sessionKey,
            onHistory:   FormatHistoryAction,
            onTool:      t => _formatter.FormatTool(t.ToolName, t.ArgsJson, t.Meta),
            onAssistant: m => _formatter.FormatAssistantMessage(m.ContentText));

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
        {
            if (!string.IsNullOrWhiteSpace(e.ContentText))
                return _formatter.FormatAssistantMessage(e.ContentText);

            if (e.ThinkingBlocks.Count > 0)
            {
                var lastTb = e.ThinkingBlocks[^1];
                return _formatter.FormatAssistantThoughts(lastTb);
            }
        }

        // User text — skip, we don't show user's own messages as "last action"
        if (e.Role == "user" && !string.IsNullOrWhiteSpace(e.ContentText))
            return null;

        return null;
    }
}

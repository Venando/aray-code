using System;
using System.Collections.Generic;

namespace ArayCode;

/// <summary>
/// A single message entry from a session's chat history.
/// </summary>
public class ChatHistoryEntry
{
    public string Role { get; init; } = "";      // "user" or "assistant"
    public string Content { get; init; } = "";
    public DateTime? CreatedAt { get; init; }

    /// <summary>
    /// Thinking content from content blocks of type "thinking" in the history response.
    /// Displayed according to <see cref="AppConfig.ThinkingDisplayMode"/>.
    /// </summary>
    public string Thinking { get; init; } = "";

    /// <summary>
    /// Tool calls associated with this entry (from content blocks of type "toolCall"
    /// or "tool_use" in the history response). Each entry has a tool name and
    /// its JSON arguments, matching the <c>OnAgentToolCall</c> event signature.
    /// </summary>
    public List<ToolCallEntry> ToolCalls { get; init; } = new();
}

/// <summary>
/// Represents a single tool invocation parsed from chat history content blocks.
/// </summary>
public class ToolCallEntry
{
    public string ToolName { get; init; } = "";
    public string Arguments { get; init; } = "";
}

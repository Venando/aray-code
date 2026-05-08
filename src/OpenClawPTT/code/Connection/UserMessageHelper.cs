using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenClawPTT;

public static class UserMessageHelper
{
    //msg example:
    /*
    {
  "role": "assistant",
  "content": [
    {
      "type": "thinking",
      "thinking": "Done! Let me check if origin/main has advanced since I created the branch."
    },
    {
      "type": "toolCall",
      "id": "call_00_qZzwfBAUSoBkQVXLrhPl3378",
      "name": "exec",
      "arguments": {
        "command": "cd ~/.openclaw/workspace/projects/streamshell/repo && git fetch origin && git merge-base --is-ancestor HEAD origin/main && echo \"up-to-date ✅\" || echo \"needs merge ⚠️\"",
        "timeout": 10
      }
    }
  ],
  "api": "openai-completions",
  "provider": "deepseek",
  "model": "deepseek-v4-flash",
  "usage": {
    "input": 292,
    "output": 126,
    "totalTokens": 62242,
    "cacheRead": 61824,
    "cacheWrite": 0,
    "cost": {
      "total": 0.001807232
    }
  },
  "stopReason": "toolUse",
  "timestamp": 1778266536980,
  "responseId": "10512b28-a199-41d8-994b-c853814589b5",
  "__openclaw": {
    "id": "1802a02b",
    "seq": 93
  }
}
    */

    public static bool TryGetChatHistoryEntry(JsonElement msg, out ChatHistoryEntry? enty)
    {
        var role = msg.TryGetProperty("role", out var r) ? r.GetString() ?? "" : "";

        // Skip system/internal messages — only show conversation (user/assistant)
        if (!string.Equals(role, "user", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
        {
            enty = null;
            return false;
        }

        var toolCalls = new List<ToolCallEntry>();
        var thinkingBlocks = new List<string>();
        var content = ExtractMessageContent(msg, toolCalls, thinkingBlocks);
        // createdAt can be:
        //   - missing → null
        //   - a Unix-millisecond number → /Date(1234567890123)/
        //   - an ISO-8601 string → DateTime.Parse
        //   - an object (message w/ role, content, etc.) → skip
        // Gateway history messages use "timestamp" (Unix ms number).
        // createdAt/created-at are not used.
        DateTime? createdAt = null;
        if (msg.TryGetProperty("timestamp", out var ts) &&
            ts.ValueKind == JsonValueKind.Number &&
            ts.TryGetInt64(out var ms))
        {
            createdAt = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        }

        // For assistant messages, allow entry even if text content is empty
        // as long as there are tool calls.
        if (string.IsNullOrWhiteSpace(content))
        {
            bool isAssistant = string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase);
            if (!isAssistant || toolCalls.Count == 0)
            {
                enty = null;
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(content) && UserMessageHelper.IsNoReply(content))
        {
            enty = null;
            return false;
        }

        content = ExtractUserMessage(content);

        enty = new ChatHistoryEntry
        {
            Role = role,
            Content = content ?? "",
            CreatedAt = createdAt,
            Thinking = string.Join("\n", thinkingBlocks).Trim(),
            ToolCalls = toolCalls,
        };
        return true;
    }

    /// <summary>
    /// Extracts text content from a message's content field (string or array of blocks).
    /// Also populates <paramref name="toolCalls"/> with tool call blocks found in the array
    /// and <paramref name="thinkingBlocks"/> with thinking blocks.
    /// </summary>
    private static string ExtractMessageContent(JsonElement msg, List<ToolCallEntry> toolCalls, List<string> thinkingBlocks)
    {
        if (!msg.TryGetProperty("content", out var contentEl))
            return "";

        if (contentEl.ValueKind == JsonValueKind.String)
            return contentEl.GetString() ?? "";

        if (contentEl.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (JsonElement block in contentEl.EnumerateArray())
            {
                if (!block.TryGetProperty("type", out var typeEl)) continue;
                var type = typeEl.GetString();

                if (type == "text" && block.TryGetProperty("text", out var textEl))
                {
                    parts.Add(textEl.GetString() ?? "");
                }
                else if (type == "thinking" && block.TryGetProperty("thinking", out var thinkEl))
                {
                    var thinkText = thinkEl.GetString() ?? "";
                    if (!string.IsNullOrEmpty(thinkText))
                        thinkingBlocks.Add(thinkText);
                }
                else if ((type == "toolCall" || type == "tool_use")
                    && block.TryGetProperty("name", out var nameEl)
                    && block.TryGetProperty("arguments", out var argsEl))
                {
                    toolCalls.Add(new ToolCallEntry
                    {
                        ToolName = nameEl.GetString() ?? "",
                        Arguments = argsEl.GetRawText(),
                    });
                }
            }
            return string.Join("", parts);
        }

        return "";
    }

    private static bool IsNoReply(string content)
    {
        if (string.IsNullOrEmpty(content)) return true;
        var trimmed = content.Trim();
        if (trimmed.Equals("NO_REPLY", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("no_reply", StringComparison.OrdinalIgnoreCase))
            return true;

        // Filter out internal context blocks injected by the system
        if (trimmed.StartsWith("<<<BEGIN_OPENCLAW_INTERNAL_CONTEXT>>>", StringComparison.Ordinal))
            return true;

        if (trimmed.StartsWith("[Startup context loaded by runtime]", StringComparison.Ordinal))
            return true;

        if (trimmed.StartsWith("Read HEARTBEAT.md if it exists(workspace context).", StringComparison.Ordinal))
            return true;

        if (trimmed.StartsWith("[Inter-session message] sourceSession=", StringComparison.Ordinal))
            return true;

        if (trimmed.Equals("HEARTBEAT_OK", StringComparison.Ordinal))
            return true;

        return false;
    }

    // Matches a full "System (untrusted): [timestamp] ...payload..." segment.
    // Payload runs until the next "System (untrusted):" or end of string.
    private static readonly Regex SystemBlockPattern = new(
        @"System \(untrusted\):\s*\[\w*\s*\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}(?::\d{2})?\s+GMT[+-]\d+\].*?(?=System \(untrusted\):|$)",
        RegexOptions.Compiled | RegexOptions.Singleline
    );

    // Matches the "An async command..." epilogue and everything after it
    private static readonly Regex SystemEpiloguePattern = new(
        @"An async command you ran earlier has completed\..*$",
        RegexOptions.Compiled | RegexOptions.Singleline
    );

    // Matches trailing time block left at the end after system blocks are stripped:
    // [Sun 2026-05-03 09:49 GMT+3]
    private static readonly Regex TrailingTimeBlockPattern = new(
        @"^\[\w*\s*\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}(?::\d{2})?\s+GMT[+-]\d+\]\s*",
        RegexOptions.Compiled
    );

    public static string ExtractUserMessage(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return input;

        if (!input.StartsWith("System (untrusted)", StringComparison.OrdinalIgnoreCase))
            return input;

        var result = input;

        // 2. Strip all "System (untrusted): [timestamp] ...payload..." blocks
        result = SystemBlockPattern.Replace(result, "");

        // 3. Strip any leftover leading time block (the user-authored timestamp
        //    that preceded user text, e.g. "[Sun 2026-05-03 09:49 GMT+3]")
        result = TrailingTimeBlockPattern.Replace(result.TrimStart(), "");

        return result.Trim();
    }

    public static bool HasUserInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        // Not a system message at all — it's pure user input
        if (!input.StartsWith("System (untrusted)", StringComparison.OrdinalIgnoreCase))
            return true;

        var userText = ExtractUserMessage(input);
        return !string.IsNullOrWhiteSpace(userText);
    }
}
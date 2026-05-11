using System.Collections.Concurrent;

namespace OpenClawPTT.Services;

/// <summary>
/// Generates conversation names by sending the first user message to a Direct LLM.
/// Tracks names per session key and clears when the active agent changes.
/// </summary>
public sealed class ConversationNamingService : IConversationNamingService, IDisposable
{
    private readonly IDirectLlmService? _directLlm;
    private readonly IColorConsole? _console;
    // Commands that reset the conversation name when executed
    private static readonly HashSet<string> ResetCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "reset",
        "new",
    };

    private readonly ConcurrentDictionary<string, string> _conversationNames = new();
    private readonly HashSet<string> _pendingSessions = new();
    private readonly object _lock = new();
    private string? _currentSessionKey;
    private bool _disposed;

    public ConversationNamingService(IDirectLlmService? directLlm, IColorConsole? console = null)
    {
        _directLlm = directLlm;
        _console = console;
        _currentSessionKey = AgentRegistry.ActiveSessionKey;
        AgentRegistry.ActiveSessionChanged += OnActiveSessionChanged;
    }

    public event Action<string?>? ConversationNameChanged;

    public string? GetCurrentConversationName()
    {
        if (_currentSessionKey == null)
            return null;
        _conversationNames.TryGetValue(_currentSessionKey, out var name);
        return name;
    }

    public void OnMessageSent(string messageText)
    {
        if (_disposed) return;

        var sessionKey = AgentRegistry.ActiveSessionKey;
        if (sessionKey == null) return;

        lock (_lock)
        {
            // Only process if we haven't already named this session and aren't currently naming it
            if (_conversationNames.ContainsKey(sessionKey) || _pendingSessions.Contains(sessionKey))
                return;

            _pendingSessions.Add(sessionKey);
        }

        // Fire off naming asynchronously — don't block the message send
        _ = GenerateNameAsync(sessionKey, messageText);
    }

    public void OnCommandSent(string commandName)
    {
        if (_disposed) return;
        if (string.IsNullOrWhiteSpace(commandName)) return;
        if (!ResetCommands.Contains(commandName)) return;

        var sessionKey = AgentRegistry.ActiveSessionKey;
        if (sessionKey == null) return;

        _conversationNames.TryRemove(sessionKey, out _);

        // Only fire event if this is still the active session
        if (AgentRegistry.ActiveSessionKey == sessionKey)
        {
            ConversationNameChanged?.Invoke(null);
        }

        _console?.Log("naming", $"Conversation name cleared by /{commandName}", LogLevel.Debug);
    }

    private async Task GenerateNameAsync(string sessionKey, string messageText)
    {
        try
        {
            if (_directLlm == null || !_directLlm.IsConfigured)
            {
                _console?.Log("naming", "Direct LLM not configured — skipping conversation naming", LogLevel.Debug);
                return;
            }

            _console?.Log("naming", "Generating conversation name...", LogLevel.Debug);

            var prompt = BuildNamingPrompt(messageText);
            var name = await _directLlm.SendAsync(prompt, CancellationToken.None);
            name = SanitizeName(name);

            if (!string.IsNullOrWhiteSpace(name) && name != "(No response)")
            {
                _conversationNames[sessionKey] = name;

                // Only fire event if this is still the active session
                if (AgentRegistry.ActiveSessionKey == sessionKey)
                {
                    ConversationNameChanged?.Invoke(name);
                }

                _console?.Log("naming", $"Conversation named: \"{name}\"", LogLevel.Debug);
            }
        }
        catch (Exception ex)
        {
            _console?.Log("naming", $"Failed to generate conversation name: {ex.Message}", LogLevel.Debug);
        }
        finally
        {
            lock (_lock)
            {
                _pendingSessions.Remove(sessionKey);
            }
        }
    }

    private static string BuildNamingPrompt(string messageText)
    {
        // Truncate very long messages to keep the prompt reasonable
        const int maxMessageLength = 500;
        var truncated = messageText.Length > maxMessageLength
            ? messageText[..maxMessageLength] + "..."
            : messageText;

        return $"Give a very short 2-4 word descriptive name for a conversation that starts with this message. Return ONLY the name, no quotes, no explanation, no punctuation at the end.\n\nMessage: {truncated}";
    }

    private static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        // Remove common wrapper text the LLM might add
        var cleaned = name.Trim();

        // Strip outer quotes first (the prefix may be inside quotes)
        for (int i = 0; i < 3; i++)
        {
            if (cleaned.StartsWith('"') && cleaned.EndsWith('"'))
                cleaned = cleaned[1..^1].Trim();
            else if (cleaned.StartsWith('\u201C') && cleaned.EndsWith('\u201D')) // smart quotes
                cleaned = cleaned[1..^1].Trim();
            else
                break;
        }

        // Remove prefixes
        if (cleaned.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned["Name:".Length..].Trim();
        if (cleaned.StartsWith("Conversation:", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned["Conversation:".Length..].Trim();

        // Remove any remaining quote characters (inner quotes around the actual name)
        cleaned = cleaned.Replace("\"", "").Replace("\u201C", "").Replace("\u201D", "").Trim();

        // Remove trailing punctuation
        cleaned = cleaned.TrimEnd('.', '!', '?', ':', ';');

        return cleaned;
    }

    private void OnActiveSessionChanged(string? sessionKey)
    {
        _currentSessionKey = sessionKey;

        if (sessionKey == null)
        {
            ConversationNameChanged?.Invoke(null);
            return;
        }

        var name = _conversationNames.TryGetValue(sessionKey, out var existingName)
            ? existingName
            : null;

        ConversationNameChanged?.Invoke(name);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            AgentRegistry.ActiveSessionChanged -= OnActiveSessionChanged;
        }
    }
}

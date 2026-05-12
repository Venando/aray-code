using System.Collections.Concurrent;
using OpenClawPTT.Services.Commands;

namespace OpenClawPTT.Services;

/// <summary>
/// Generates conversation names by sending the first user message to a Direct LLM.
/// Tracks names per session key and clears when the active agent changes.
/// </summary>
public sealed class ConversationNamingService : IConversationNamingService, IDisposable
{
    private readonly IDirectLlmService? _directLlm;
    private readonly IColorConsole? _console;

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
        if (_currentSessionKey == null) return null;
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
            if (_conversationNames.ContainsKey(sessionKey) || _pendingSessions.Contains(sessionKey))
                return;

            _pendingSessions.Add(sessionKey);
        }

        _ = GenerateNameAsync(sessionKey, messageText);
    }

    public void OnCommandExecuted(object? sender, CommandExecutedEventArgs e)
    {
        if (_disposed) return;
        if (string.IsNullOrWhiteSpace(e.Name)) return;

        // Clear conversation name on session reset commands
        // Check both the name and the type for robustness
        var isResetCommand = e.Type == OpenClawPTT.Services.Commands.ShellCommandType.SessionControl &&
            (e.Name.Equals("reset", StringComparison.OrdinalIgnoreCase) ||
             e.Name.Equals("new", StringComparison.OrdinalIgnoreCase));

        if (!isResetCommand) return;

        var sessionKey = AgentRegistry.ActiveSessionKey;
        if (sessionKey == null) return;

        _conversationNames.TryRemove(sessionKey, out _);

        if (AgentRegistry.ActiveSessionKey == sessionKey)
        {
            ConversationNameChanged?.Invoke(null);
        }

        _console?.Log("naming", $"Conversation name cleared by /{e.Name}", LogLevel.Debug);
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

        var cleaned = name.Trim();

        for (int i = 0; i < 3; i++)
        {
            if (cleaned.StartsWith('"') && cleaned.EndsWith('"'))
                cleaned = cleaned[1..^1].Trim();
            else if (cleaned.StartsWith('\u201C') && cleaned.EndsWith('\u201D'))
                cleaned = cleaned[1..^1].Trim();
            else
                break;
        }

        if (cleaned.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned["Name:".Length..].Trim();
        if (cleaned.StartsWith("Conversation:", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned["Conversation:".Length..].Trim();

        cleaned = cleaned.Replace("\"", "").Replace("\u201C", "").Replace("\u201D", "").Trim();
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

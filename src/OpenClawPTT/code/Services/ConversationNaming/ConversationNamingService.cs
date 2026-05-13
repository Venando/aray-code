using System.Collections.Concurrent;
using System.Text;
using OpenClawPTT.Services.Commands;

namespace OpenClawPTT.Services;

/// <summary>
/// Generates conversation names by analyzing multiple messages from both
/// user and agent (including session history) to produce adaptive titles.
///
/// Instead of naming from the first message alone, it:
///   - Accumulates user messages and agent replies for the current session
///   - Incorporates session history (past messages) as context
///   - Regenerates the name adaptively as the conversation evolves
///   - Uses a richer prompt with conversation flow context
/// </summary>
public sealed class ConversationNamingService : IConversationNamingService, IDisposable
{
    private readonly IDirectLlmService? _directLlm;
    private readonly IColorConsole? _console;
    private readonly AppConfig _appConfig;
    private readonly CancellationTokenSource _cts = new();

    // ── Constants ──────────────────────────────────────────────────────────────

    /// <summary>Max length of a single message in the prompt.</summary>
    private const int MaxMessageLength = 400;

    /// <summary>How many messages (user + agent) trigger the first naming attempt.
    /// Set to 1 so a single user message immediately triggers naming (backward compat.
    /// with the original behavior). Adaptive re-naming happens as more messages arrive.</summary>
    private const int InitialNamingThreshold = 1;

    /// <summary>After this many messages, re-evaluate the name.</summary>
    private const int ReNamingThreshold = 6;

    /// <summary>Maximum number of renames per session to avoid churn.</summary>
    private const int MaxRenamesPerSession = 3;

    /// <summary>Maximum recent messages to include in the prompt.</summary>
    private const int MaxPromptMessages = 8;

    /// <summary>Maximum session history messages to include as context.</summary>
    private const int MaxHistoryContextMessages = 4;

    // ── State ──────────────────────────────────────────────────────────────────

    private readonly ConcurrentDictionary<string, string> _conversationNames = new();
    private readonly HashSet<string> _pendingSessions = new();
    private string? _currentSessionKey;
    private bool _disposed;

    // Per-session state stored in a concurrent dictionary for thread safety
    private readonly ConcurrentDictionary<string, SessionNamingState> _sessionStates = new();

    private sealed class SessionNamingState
    {
        /// <summary>Ordered list of (role, content) messages in the current conversation.</summary>
        public List<(string Role, string Content)> Messages { get; } = new();

        /// <summary>List of session history entries (from before this conversation).</summary>
        public List<ChatHistoryEntry>? History { get; set; }

        /// <summary>How many times the name has been regenerated for this session.</summary>
        public int RenameCount { get; set; }

        /// <summary>Whether initial naming has completed.</summary>
        public bool HasName { get; set; }
    }

    // ── Construction ───────────────────────────────────────────────────────────

    public ConversationNamingService(IDirectLlmService? directLlm, AppConfig appConfig, IColorConsole? console = null)
    {
        _directLlm = directLlm;
        _appConfig = appConfig;
        _console = console;
        _currentSessionKey = AgentRegistry.ActiveSessionKey;
        AgentRegistry.ActiveSessionChanged += OnActiveSessionChanged;
    }

    public event Action<string?>? ConversationNameChanged;

    // ── IConversationNamingService ─────────────────────────────────────────────

    public string? GetCurrentConversationName()
    {
        if (_currentSessionKey == null) return null;
        _conversationNames.TryGetValue(_currentSessionKey, out var name);
        return name;
    }

    public void OnMessageSent(string messageText)
    {
        if (string.IsNullOrWhiteSpace(messageText) || messageText.StartsWith('/') || _disposed)
            return;

        var sessionKey = AgentRegistry.ActiveSessionKey;
        if (sessionKey == null) return;

        var state = _sessionStates.GetOrAdd(sessionKey, _ => new SessionNamingState());

        lock (state)
        {
            state.Messages.Add(("user", TruncateMessage(messageText)));
        }

        _console?.Log("naming", $"Message tracked ({state.Messages.Count} total)", LogLevel.Debug);

        MaybeTriggerNaming(sessionKey, state);
    }

    public void OnAgentReplyReceived(string replyText)
    {
        if (string.IsNullOrWhiteSpace(replyText) || _disposed)
            return;

        var sessionKey = AgentRegistry.ActiveSessionKey;
        if (sessionKey == null) return;

        var state = _sessionStates.GetOrAdd(sessionKey, _ => new SessionNamingState());

        lock (state)
        {
            state.Messages.Add(("assistant", TruncateMessage(replyText)));
        }

        _console?.Log("naming", $"Agent reply tracked ({state.Messages.Count} total)", LogLevel.Debug);

        MaybeTriggerNaming(sessionKey, state);
    }

    public void SetSessionHistory(List<ChatHistoryEntry>? history)
    {
        if (history == null || history.Count == 0 || _disposed) return;

        var sessionKey = AgentRegistry.ActiveSessionKey;
        if (sessionKey == null) return;

        var state = _sessionStates.GetOrAdd(sessionKey, _ => new SessionNamingState());

        lock (state)
        {
            state.History = history;
        }

        _console?.Log("naming", $"Session history set ({history.Count} entries)", LogLevel.Debug);
    }

    public void OnCommandExecuted(object? sender, CommandExecutedEventArgs e)
    {
        if (_disposed) return;
        if (string.IsNullOrWhiteSpace(e.Name)) return;

        // Clear conversation name on session reset commands
        var isResetCommand = e.Type == ShellCommandType.SessionControl &&
            (e.Name.Equals("reset", StringComparison.OrdinalIgnoreCase) ||
             e.Name.Equals("new", StringComparison.OrdinalIgnoreCase));

        if (!isResetCommand) return;

        var sessionKey = AgentRegistry.ActiveSessionKey;
        if (sessionKey == null) return;

        _conversationNames.TryRemove(sessionKey, out _);
        _sessionStates.TryRemove(sessionKey, out _);

        if (AgentRegistry.ActiveSessionKey == sessionKey)
        {
            ConversationNameChanged?.Invoke(null);
        }

        _console?.Log("naming", $"Conversation name cleared by /{e.Name}", LogLevel.Debug);
    }

    // ── Naming logic ───────────────────────────────────────────────────────────

    private void MaybeTriggerNaming(string sessionKey, SessionNamingState state)
    {
        lock (state)
        {
            if (!state.HasName)
            {
                // First name: wait until we have at least one user message and optionally an agent reply
                if (state.Messages.Count < InitialNamingThreshold)
                    return;

                state.HasName = true;
            }
            else
            {
                // Re-evaluate: only if we've crossed the threshold and haven't renamed too many times
                if (state.RenameCount >= MaxRenamesPerSession)
                    return;

                // Count non-overlapping segments of ReNamingThreshold messages
                // Since we already named, subtract the initial threshold from the count
                int messagesSinceLastName = state.Messages.Count - InitialNamingThreshold;

                // The rename count tells us how many renames we've done.
                // Each rename happens after ReNamingThreshold more messages.
                int expectedMessagesForCurrentRenameCount = InitialNamingThreshold +
                    (state.RenameCount * ReNamingThreshold);

                if (state.Messages.Count < expectedMessagesForCurrentRenameCount + ReNamingThreshold)
                    return;

                state.RenameCount++;
            }
        }

        // Check if this session already has a pending generation
        lock (_lockObj)
        {
            if (_pendingSessions.Contains(sessionKey))
                return;
            _pendingSessions.Add(sessionKey);
        }

        _ = GenerateNameAsync(sessionKey, state);
    }

    private static readonly object _lockObj = new();

    private async Task GenerateNameAsync(string sessionKey, SessionNamingState state)
    {
        try
        {
            if (_directLlm == null || !_directLlm.IsConfigured)
            {
                _console?.Log("naming", "Direct LLM not configured — skipping conversation naming", LogLevel.Debug);
                return;
            }

            _console?.Log("naming", "Generating conversation name...", LogLevel.Debug);

            var prompt = BuildNamingPrompt(state);
            var name = await _directLlm.SendAsync(prompt, _cts.Token);
            name = SanitizeName(name);

            if (!string.IsNullOrWhiteSpace(name) && name != "(No response)")
            {
                _conversationNames[sessionKey] = name;

                if (AgentRegistry.ActiveSessionKey == sessionKey)
                {
                    ConversationNameChanged?.Invoke(name);
                }

                _console?.Log("naming", $"Conversation named: \"{name}\" (rename #{state.RenameCount})", LogLevel.Debug);
            }
        }
        catch (Exception ex)
        {
            _console?.Log("naming", $"Failed to generate conversation name: {ex.Message}", LogLevel.Debug);
        }
        finally
        {
            lock (_lockObj)
            {
                _pendingSessions.Remove(sessionKey);
            }
        }
    }

    /// <summary>
    /// Builds a prompt that includes session history (if available) and
    /// recent messages from the current conversation, producing a richer
    /// context for the LLM to generate an appropriate title.
    /// </summary>
    private string BuildNamingPrompt(SessionNamingState state)
    {
        var sb = new StringBuilder();

        // --- History context (past messages before this session) ---
        if (state.History is { Count: > 0 })
        {
            int historyCount = Math.Min(state.History.Count, MaxHistoryContextMessages);
            sb.AppendLine("Previous conversation context (from before this session):");

            for (int i = state.History.Count - historyCount; i < state.History.Count; i++)
            {
                var entry = state.History[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Content))
                    continue;

                var role = entry.Role?.ToLowerInvariant() == "user" ? "User" : "Assistant";
                var content = entry.Content.Length > 300
                    ? entry.Content[..300] + "..."
                    : entry.Content;
                sb.AppendLine($"  {role}: {content}");
            }

            sb.AppendLine();
        }

        // --- Current conversation ---
        sb.AppendLine("Current conversation:");

        int startIdx = Math.Max(0, state.Messages.Count - MaxPromptMessages);
        for (int i = startIdx; i < state.Messages.Count; i++)
        {
            var (role, content) = state.Messages[i];
            var label = role == "user" ? "User" : "Assistant";
            sb.AppendLine($"  {label}: {content}");
        }

        sb.AppendLine();

        // --- Naming instruction ---
        var template = _appConfig.ConversationNamingPrompt;
        if (string.IsNullOrWhiteSpace(template))
        {
            template = "Give a very short 4-6 word descriptive name for this conversation based on all the messages above (history context and current discussion). Focus on the current direction of the conversation. Return ONLY the name, no quotes, no explanation, no punctuation at the end.";
        }
        else
        {
            // Replace {message} for backward compatibility with old templates
            // Also inject the conversation context
        }

        sb.Append(template);

        return sb.ToString();
    }

    private static string TruncateMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        return text.Length > MaxMessageLength
            ? text[..MaxMessageLength] + "..."
            : text;
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

        // Restore name if it exists for this session
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
            _cts.Cancel();
            _cts.Dispose();
            AgentRegistry.ActiveSessionChanged -= OnActiveSessionChanged;
        }
    }
}

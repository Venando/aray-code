using System.Collections.Concurrent;
using System.Text.Json;
using OpenClawPTT.Services;
using OpenClawPTT.Services.Diagnostics;

namespace OpenClawPTT;

/// <summary>
/// Handles session-related gateway events: session.message, agent, and chat.
/// Extracts content blocks (text, thinking, tool calls, audio) and fires
/// the appropriate events through <see cref="IGatewayEventSource"/>.
///
/// Also detects model fallback: when the primary model fails with an error,
/// the gateway switches to a fallback. The next successful session.message
/// carries the new provider/model in the message envelope itself, so we can
/// detect the change without any RPC call.
/// </summary>
public class SessionMessageHandler : IEventHandler<SessionMessageEvent>
{
    private readonly IGatewayEventSource _events;
    private readonly AppConfig _cfg;
    private readonly IColorConsole _console;

    // Per-session error tracking for fallback detection.
    // After an error, we record the failed provider/model. When a successful
    // response arrives on the same session, we compare the message's provider
    // against the recorded error provider to detect a fallback.
    // Uses ConcurrentDictionary for thread safety across concurrent sessions.
    private static readonly ConcurrentDictionary<string, SessionErrorState> _sessionErrors =
        new(StringComparer.Ordinal);

    private sealed record SessionErrorState(
        string? Provider,
        string? Model,
        string? ErrorMessage,
        bool FallbackNotifiedForRun);

    /// <summary>
    /// Builds a compound key for per-session error tracking.
    /// Includes agentId when available so errors from different agents
    /// on the same session key are tracked independently.
    /// </summary>
    private static string ErrorStateKey(string? sessionKey, string? agentId)
    {
        if (sessionKey == null) return string.Empty;
        return agentId != null ? $"{sessionKey}:{agentId}" : sessionKey;
    }

    public SessionMessageHandler(
        IGatewayEventSource events,
        AppConfig cfg,
        IColorConsole? console = null)
    {
        _events = events;
        _cfg = cfg;
        _console = console ?? new ColorConsole(new StreamShellHost());
    }

    public Task HandleAsync(SessionMessageEvent evt)
    {
        switch (evt.EventName)
        {
            case "session.message":
                HandleSessionMessage(evt.Payload);
                break;
            case "agent":
                HandleAgentStream(evt.Payload);
                break;
            case "chat":
                HandleChatFinal(evt.Payload);
                break;
        }

        return Task.CompletedTask;
    }

    private void HandleSessionMessage(JsonElement payload)
    {
        var sessionKey = payload.TryGetProperty("sessionKey", out var skEl)
            ? skEl.GetString() : null;
        var agentId = payload.TryGetProperty("agentId", out var aEl)
            ? aEl.GetString() : null;

        if (!payload.TryGetProperty("message", out var messageEl)) return;
        if (!messageEl.TryGetProperty("role", out var roleEl)) return;
        if (roleEl.GetString() != "assistant") return;

        // Check for error messages (stopReason=error, content may be empty)
        // When stopReason=error, the message carries the provider that FAILED,
        // not a fallback. Fallback detection is handled after successful responses.
        if (messageEl.TryGetProperty("stopReason", out var stopReason) && stopReason.GetString() == "error")
        {
            HandleErrorMessage(messageEl, sessionKey, agentId);
            return;
        }

        if (!messageEl.TryGetProperty("content", out var contentEl)) return;
        if (contentEl.ValueKind != JsonValueKind.Array) return;

        // In realtime mode, HandleAgentStream owns the delta lifecycle (phase=start/end).
        // HandleSessionMessage fires content events only — no double delta framing.
        bool emitDelta = !_cfg.RealTimeReplyOutput;
        bool startFired = false;

        foreach (var block in contentEl.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var typeEl)) continue;
            var type = typeEl.GetString();

            if (type == "thinking" && block.TryGetProperty("thinking", out var thinkingEl))
            {
                var thinking = thinkingEl.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(thinking))
                    _events.RaiseAgentThinking(thinking);
            }
            else if (type == "toolCall" && block.TryGetProperty("name", out var nameEl) && block.TryGetProperty("arguments", out var argsEl))
            {
                var toolName = nameEl.GetString() ?? string.Empty;
                var args = argsEl.GetRawText();
                var toolCallId = block.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                var rawBlock = block.GetRawText();
                _console.Log("debug", $"ToolCall: id={toolCallId} name={toolName} args={args} raw={rawBlock}", LogLevel.Debug);
                _events.RaiseAgentToolCall(toolName, args);
            }
            else if (type == "text" && block.TryGetProperty("text", out var textEl))
            {
                var text = textEl.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(text))
                {
                    if (emitDelta && !startFired)
                    {
                        _events.RaiseAgentReplyDeltaStart();
                        startFired = true;
                    }

                    _events.RaiseAgentReplyFull(text);
                }
            }
            else if (type == "audio" && block.TryGetProperty("audio", out var audioEl))
            {
                // Audio blocks are silently dropped — no longer surfaced to consumers.
            }
            else
            {
                _console.Log("debug", $"Unknown block type=\"{type}\": {block}");
            }
        }

        if (emitDelta && startFired)
            _events.RaiseAgentReplyDeltaEnd();

        // Detect fallback by comparing the message's provider to the errored provider.
        // No RPC needed — the message itself carries the provider that produced it.
        DetectFallbackFromMessage(sessionKey, messageEl, agentId);
    }

    /// <summary>
    /// Records an error for this session. Called when stopReason=error.
    /// The actual display of the error is handled by HandleAgentStream
    /// (phase=error lifecycle event) — this method only records state
    /// for later fallback detection.
    /// </summary>
    private void HandleErrorMessage(JsonElement messageEl, string? sessionKey, string? agentId)
    {
        var errorMessage = messageEl.TryGetProperty("errorMessage", out var errEl)
            ? errEl.GetString() ?? "Unknown error"
            : "Unknown error";
        var provider = messageEl.TryGetProperty("provider", out var provEl)
            ? provEl.GetString() : null;
        var model = messageEl.TryGetProperty("model", out var modEl)
            ? modEl.GetString() : null;

        var providerModel = provider != null ? $"{provider}/{model}" : "?";
        _console.Log("debug", $"{providerModel} stopReason=error: {errorMessage}", LogLevel.Debug);

        // Record error state for fallback detection (per-session, per-agent)
        var errorKey = ErrorStateKey(sessionKey, agentId);
        if (errorKey.Length > 0)
        {
            _sessionErrors[errorKey] = new SessionErrorState(provider, model, errorMessage, FallbackNotifiedForRun: false);
        }
    }

    /// <summary>
    /// Detects whether a model fallback occurred by comparing the current
    /// message's provider/model against the recorded error provider.
    /// The message envelope carries the provider that actually produced
    /// the response — no RPC call needed.
    /// </summary>
    private void DetectFallbackFromMessage(string? sessionKey, JsonElement messageEl, string? agentId)
    {
        var errorKey = ErrorStateKey(sessionKey, agentId);
        if (errorKey.Length == 0) return;
        if (!_sessionErrors.TryGetValue(errorKey, out var state)) return;
        if (state.FallbackNotifiedForRun) return;

        var currentProvider = messageEl.TryGetProperty("provider", out var cp) ? cp.GetString() : null;
        var currentModel = messageEl.TryGetProperty("model", out var cm) ? cm.GetString() : null;

        if (currentProvider == null || currentModel == null)
            return;

        // Skip gateway-injected system messages (e.g. "Model reset to default...").
        // These carry provider="openclaw/gateway-injected" and are not actual model responses,
        // so comparing them against the errored provider would produce a false fallback.
        if (currentProvider.Contains("gateway-injected", StringComparison.OrdinalIgnoreCase))
            return;

        // If provider is the same, no fallback — the errored model handled it successfully
        if (string.Equals(currentProvider, state.Provider, StringComparison.OrdinalIgnoreCase))
        {
            _sessionErrors.TryUpdate(errorKey, state with { FallbackNotifiedForRun = true }, state);
            return;
        }

        // Different provider = the gateway switched after the error (auto-fallback)
        _console.PrintModelFallback(
            state.Provider ?? "?",
            state.Model ?? "?",
            currentProvider,
            currentModel,
            isQuotaError: IsQuotaError(state.ErrorMessage ?? ""));

        _sessionErrors.TryUpdate(errorKey, state with { FallbackNotifiedForRun = true }, state);
    }

    private void HandleAgentStream(JsonElement payload)
    {
        if (!payload.TryGetProperty("data", out var data)) return;

        // Handle lifecycle events
        if (data.TryGetProperty("phase", out var phase))
        {
            var phaseType = phase.GetString() ?? string.Empty;

            if (phaseType == "start")
            {
                if (_cfg.RealTimeReplyOutput)
                    _events.RaiseAgentReplyDeltaStart();
            }
            else if (phaseType == "end")
            {
                if (_cfg.RealTimeReplyOutput)
                    _events.RaiseAgentReplyDeltaEnd();
            }
            else if (phaseType == "error")
            {
                var errorMsg = data.TryGetProperty("error", out var e)
                    ? e.GetString() ?? "Unknown agent error"
                    : "Unknown agent error";

                _console.Log("debug", $"Agent lifecycle error: {errorMsg}", LogLevel.Debug);

                if (IsQuotaError(errorMsg))
                {
                    _console.PrintModelFailed(errorMsg);
                }
                else if (errorMsg.Contains("fallback", StringComparison.OrdinalIgnoreCase) ||
                         errorMsg.Contains("failover", StringComparison.OrdinalIgnoreCase))
                {
                    _console.PrintWarning($"Agent: {errorMsg}");
                }
                else
                {
                    _console.PrintError($"Agent error: {errorMsg}");

                    if (_cfg.RealTimeReplyOutput)
                    {
                        _events.RaiseAgentReplyDeltaStart();
                        _events.RaiseAgentReplyFull(errorMsg);
                        _events.RaiseAgentReplyFinal(errorMsg);
                        _events.RaiseAgentReplyDeltaEnd();
                    }
                }
            }
        }

        // Handle streaming delta (only in realtime mode)
        if (_cfg.RealTimeReplyOutput && data.TryGetProperty("delta", out var delta))
        {
            var chunk = delta.GetString() ?? string.Empty;
            if (!string.IsNullOrEmpty(chunk))
                _events.RaiseAgentReplyDelta(chunk);
        }
    }

    private void HandleChatFinal(JsonElement payload)
    {
        if (!payload.TryGetProperty("state", out var state)) return;
        if (state.GetString() != "final") return;

        if (_cfg.RealTimeReplyOutput)
            return;

        if (!payload.TryGetProperty("message", out var messageEl)) return;
        if (!messageEl.TryGetProperty("content", out var contentEl)) return;

        var text = ExtractFullText(contentEl);
        if (!string.IsNullOrEmpty(text))
        {
            _events.RaiseAgentReplyFinal(text);
        }
    }

    private static string ExtractFullText(JsonElement contentElement)
    {
        if (contentElement.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var textParts = new List<string>();
        foreach (var item in contentElement.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var typeElement) &&
                typeElement.GetString() == "text" &&
                item.TryGetProperty("text", out var textElement))
            {
                textParts.Add(textElement.GetString() ?? string.Empty);
            }
        }
        return string.Join("", textParts);
    }

    private static bool IsQuotaError(string message)
        => GatewayErrorClassifier.IsQuotaError(message);
}

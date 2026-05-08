using System.Text.Json;
using OpenClawPTT.Services;

namespace OpenClawPTT;

/// <summary>
/// Handles session-related gateway events: session.message, agent, and chat.
/// Extracts content blocks (text, thinking, tool calls, audio) and fires
/// the appropriate events through <see cref="IGatewayEventSource"/>.
///
/// Also detects model fallback: when the primary model fails with an error,
/// this handler calls sessions.describe after the next successful response
/// to determine if the gateway performed an automatic fallback (modelOverrideSource="auto")
/// or the user intentionally switched models (modelOverrideSource="user").
/// Only auto-fallbacks trigger a user-visible notification.
/// </summary>
public class SessionMessageHandler : IEventHandler<SessionMessageEvent>
{
    private readonly IGatewayEventSource _events;
    private readonly IRpcCaller _rpc;
    private readonly AppConfig _cfg;
    private readonly IContentExtractor _contentExtractor;
    private readonly IColorConsole _console;

    // Per-session error tracking for fallback detection.
    // Keyed by session key so multi-agent setups are handled correctly.
    // After an error, we record the failed provider/model/error message.
    // When a successful response arrives on the same session, we call
    // sessions.describe to determine if it was an auto-fallback.
    private string? _lastErrorProvider;
    private string? _lastErrorModel;
    private string? _lastErrorMessage;
    private string? _lastErrorSessionKey;
    private bool _fallbackNotifiedForRun;

    public SessionMessageHandler(
        IGatewayEventSource events,
        IRpcCaller rpc,
        AppConfig cfg,
        IContentExtractor? contentExtractor = null,
        IColorConsole? console = null)
    {
        _events = events;
        _rpc = rpc;
        _cfg = cfg;
        _contentExtractor = contentExtractor ?? new ContentExtractor();
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

        if (!payload.TryGetProperty("message", out var messageEl)) return;
        if (!messageEl.TryGetProperty("role", out var roleEl)) return;
        if (roleEl.GetString() != "assistant") return;

        // Check for error messages (stopReason=error, content may be empty)
        if (messageEl.TryGetProperty("stopReason", out var stopReason) && stopReason.GetString() == "error")
        {
            HandleErrorMessage(messageEl, sessionKey);
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
                _console.Log("debug", $"ToolCall: {toolName}({args})", LogLevel.Debug);
                _events.RaiseAgentToolCall(toolName, args);
            }
            else if (type == "text" && block.TryGetProperty("text", out var textEl))
            {
                var text = textEl.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(text))
                {
                    var (hasAudio, hasText, audioText, textContent) = _contentExtractor.ExtractMarkedContent(text);
                    if (hasAudio)
                        _events.RaiseAgentReplyAudio(audioText);
                    if (emitDelta && !startFired)
                    {
                        _events.RaiseAgentReplyDeltaStart();
                        startFired = true;
                    }
                    if (hasText)
                        _events.RaiseAgentReplyFull(textContent);
                    else if (hasAudio)
                        _events.RaiseAgentReplyFull(_contentExtractor.StripAudioTags(text));
                }
            }
            else if (type == "audio" && block.TryGetProperty("audio", out var audioEl))
            {
                var audioText = audioEl.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(audioText))
                {
                    if (emitDelta && !startFired)
                    {
                        _events.RaiseAgentReplyDeltaStart();
                        startFired = true;
                    }
                    _events.RaiseAgentReplyAudio(audioText);
                }
            }
            else
            {
                _console.Log("debug", $"Unknown block type=\"{type}\": {block}");
            }
        }

        if (emitDelta && startFired)
            _events.RaiseAgentReplyDeltaEnd();

        // After a successful non-error response, check if a fallback occurred.
        // Only act if we previously saw an error on this session and haven't
        // yet notified for this fallback run.
        if (_lastErrorProvider != null && !_fallbackNotifiedForRun)
        {
            _ = CheckAndNotifyFallbackAsync(sessionKey);
        }
    }

    /// <summary>
    /// Records an error for this session. Called when stopReason=error.
    /// The actual display of the error is handled by HandleAgentStream
    /// (phase=error lifecycle event) — this method only records state
    /// for later fallback detection.
    /// </summary>
    private void HandleErrorMessage(JsonElement messageEl, string? sessionKey)
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

        // Record error state for fallback detection
        _lastErrorProvider = provider;
        _lastErrorModel = model;
        _lastErrorMessage = errorMessage;
        _lastErrorSessionKey = sessionKey;
        _fallbackNotifiedForRun = false;
    }

    /// <summary>
    /// After a successful response following an error, calls sessions.describe
    /// to determine if the gateway performed an automatic fallback.
    /// Shows a fallback notification only when modelOverrideSource="auto".
    /// </summary>
    private async Task CheckAndNotifyFallbackAsync(string? sessionKey)
    {
        // Ignore if session key doesn't match (prevents cross-session contamination)
        if (sessionKey != _lastErrorSessionKey || _fallbackNotifiedForRun)
            return;

        try
        {
            // sessions.describe returns the SessionEntry for a session.
            // SessionEntry.modelOverrideSource="auto" means the gateway automatically
            // switched models (fallback). "user" means user intentionally switched.
            var result = await _rpc.SendEventAsync("sessions.describe", new Dictionary<string, object?>
            {
                ["key"] = sessionKey ?? "main"
            }, CancellationToken.None);

            if (result.ValueKind == JsonValueKind.Undefined || result.ValueKind == JsonValueKind.Null)
            {
                // Response was empty — don't retry forever
                _fallbackNotifiedForRun = true;
                return;
            }

            _console.Log("debug", $"sessions.describe [{sessionKey}]: {result.ToString()[..Math.Min(result.ToString().Length, 600)]}", LogLevel.Debug);

            var overrideSource = result.TryGetProperty("modelOverrideSource", out var src)
                ? src.GetString() : null;

            if (overrideSource == "auto")
            {
                // Auto-fallback: the gateway switched models automatically.
                // Show fallback notification with the original (failed) and
                // current (active) provider/model details.
                var currentProvider = result.TryGetProperty("modelProvider", out var cp) ? cp.GetString() : null;
                var currentModel = result.TryGetProperty("model", out var cm) ? cm.GetString() : null;

                _console.PrintModelFallback(
                    _lastErrorProvider ?? "?",
                    _lastErrorModel ?? "?",
                    currentProvider ?? "?",
                    currentModel ?? "?",
                    isQuotaError: IsQuotaError(_lastErrorMessage ?? ""));
            }
            // else if overrideSource == "user": user intentionally switched — no notification

            _fallbackNotifiedForRun = true;
        }
        catch (Exception ex)
        {
            _console.Log("debug", $"sessions.describe fallback check: {ex.Message}", LogLevel.Debug);
            // Ensure we don't spam retries on persistent failure
            _fallbackNotifiedForRun = true;
        }
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
                // Gateway sends lifecycle error events to WebSocket clients.
                // Show the error message immediately regardless of reply mode.
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

                    // Also notify adapter for display
                    if (_cfg.RealTimeReplyOutput)
                    {
                        _events.RaiseAgentReplyDeltaStart();
                        _events.RaiseAgentReplyFull(errorMsg);
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
        {
            // In realtime mode, streaming responses are already handled by HandleAgentStream
            // HandleChatFinal should be silent (no double End)
            return;
        }

        if (!payload.TryGetProperty("message", out var messageEl)) return;
        if (!messageEl.TryGetProperty("content", out var contentEl)) return;

        var text = ExtractFullText(contentEl);
        if (!string.IsNullOrEmpty(text))
        {
            _events.RaiseAgentReplyDeltaStart();
            _events.RaiseAgentReplyFull(text);
            _events.RaiseAgentReplyDeltaEnd();
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
    {
        return message.Contains("usage limit", StringComparison.OrdinalIgnoreCase)
            || message.Contains("quota", StringComparison.OrdinalIgnoreCase)
            || message.Contains("insufficient funds", StringComparison.OrdinalIgnoreCase)
            || message.Contains("billing cycle", StringComparison.OrdinalIgnoreCase)
            || message.Contains("billing error", StringComparison.OrdinalIgnoreCase)
            || message.Contains("insufficient balance", StringComparison.OrdinalIgnoreCase);
    }
}

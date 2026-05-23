using System;
using System.Collections.Generic;
using System.Text.Json;

namespace OpenClawPTT.Services;

/// <summary>
/// Subset of IGatewayService events relevant for UI-layer adapters.
/// Allows UiEventAdapter to be tested in isolation without a real GatewayService.
/// </summary>
public interface IGatewayUIEvents
{
    /// <summary>Fires after a successful connection to the gateway (initial or reconnection).</summary>
    event Action? Connected;

    /// <summary>Fires when the WebSocket connection is lost.</summary>
    event Action? Disconnected;

    /// <summary>Fires when the reconnection loop begins after an unexpected disconnect.</summary>
    event Action? Reconnecting;

    /// <summary>Fires when the reconnection loop exhausts all retries without success.</summary>
    event Action? ReconnectFailed;

    event Action<string, string?>? AgentReplyFull; // (text, stopReason)
    event Action<string>? AgentReplyFinal;
    event Action? AgentReplyDeltaStart;
    event Action<string>? AgentReplyDelta;
    event Action? AgentReplyDeltaEnd;
    event Action<string>? AgentThinking;
    event Action<string, string>? AgentToolCall;
    event Action<string, JsonElement>? EventReceived;
}

public interface IGatewayService : IDisposable, IGatewayUIEvents
{
    Task ConnectAsync(CancellationToken ct = default);
    Task SendTextAsync(string text, CancellationToken ct = default);
    void RecreateWithConfig(AppConfig newConfig);

    /// <summary>
    /// Recreates the TTS provider and audio handler with updated configuration.
    /// Called when TTS-related config properties change via /reconfigure.
    /// </summary>
    Task RecreateTtsProviderAsync(AppConfig newConfig);

    /// <summary>Sends a generic RPC request to the gateway.</summary>
    Task<JsonElement> SendRpcAsync(string method, object? parameters, CancellationToken ct = default);

    /// <summary>Fetches recent chat history for a session. Returns null if unavailable.</summary>
    Task<List<ChatHistoryEntry>?> FetchSessionHistoryAsync(string sessionKey, int limit = 5);

    /// <summary>Display text using the AgentOutputCoordinator rendering pipeline (word-wrap, markdown conversion, agent prefix).</summary>
    void DisplayAssistantReply(string body);

    /// <summary>
    /// Displays a full chat history entry: tool calls first (via ToolDisplayHandler),
    /// then the assistant reply text (via <see cref="DisplayAssistantReply"/>).
    /// </summary>
    void DisplayHistoryEntry(ChatHistoryEntry entry);

    /// <summary>
    /// Sets a callback that reports TTS synthesis runtime status.
    /// Called with <c>true</c> on success, <c>false</c> on failure.
    /// Used by <see cref="AppRunner"/> to keep the TTS status dot accurate.
    /// </summary>
    Action<bool>? OnTtsSynthesisStatus { set; }
}
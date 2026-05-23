using System;
using System.Text.Json;

namespace OpenClawPTT;

public interface IGatewayEventSource
{
    event Action<string, JsonElement>? EventReceived;
    event Action<string, string?>? AgentReplyFull; // (text, stopReason)
    event Action<string>? AgentReplyDelta;
    event Action<string>? AgentReplyFinal;
    event Action? AgentReplyDeltaStart;
    event Action? AgentReplyDeltaEnd;
    event Action<string>? AgentThinking;
    event Action<string, string>? AgentToolCall;
    /// <summary>Fires when the WebSocket connection is lost (server close, network error, etc.).</summary>
    event Action? Disconnected;

    // Raise helpers — allows external callers to fire events without violating C# event-access rules
    void RaiseAgentThinking(string thinking);
    void RaiseAgentToolCall(string toolName, string arguments);
    void RaiseAgentReplyDeltaStart();
    void RaiseAgentReplyDeltaEnd();
    void RaiseAgentReplyFull(string text, string? stopReason = null);
    void RaiseAgentReplyFinal(string text);
    void RaiseAgentReplyDelta(string chunk);
    void RaiseEventReceived(string eventName, JsonElement payload);

    /// <summary>Fires <see cref="Disconnected"/> to signal connection loss to subscribers.</summary>
    void RaiseDisconnected();
}

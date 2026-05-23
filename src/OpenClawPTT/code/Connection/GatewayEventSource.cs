using System;
using System.Text.Json;

namespace OpenClawPTT;

public sealed class GatewayEventSource : IGatewayEventSource
{
    public event Action<string, JsonElement>? EventReceived;
    public event Action<string, string?>? AgentReplyFull; // (text, stopReason)
    public event Action<string>? AgentReplyFinal;
    public event Action<string>? AgentReplyDelta;
    public event Action? AgentReplyDeltaStart;
    public event Action? AgentReplyDeltaEnd;
    public event Action<string>? AgentThinking;
    public event Action<string, string>? AgentToolCall;
    public event Action? Disconnected;

    public void RaiseAgentThinking(string thinking) => AgentThinking?.Invoke(thinking);
    public void RaiseAgentToolCall(string toolName, string arguments) => AgentToolCall?.Invoke(toolName, arguments);
    public void RaiseAgentReplyDeltaStart() => AgentReplyDeltaStart?.Invoke();
    public void RaiseAgentReplyDeltaEnd() => AgentReplyDeltaEnd?.Invoke();
    public void RaiseAgentReplyFull(string text, string? stopReason = null) => AgentReplyFull?.Invoke(text, stopReason);
    public void RaiseAgentReplyFinal(string text) => AgentReplyFinal?.Invoke(text);
    public void RaiseAgentReplyDelta(string chunk) => AgentReplyDelta?.Invoke(chunk);
    public void RaiseEventReceived(string eventName, JsonElement payload) => EventReceived?.Invoke(eventName, payload);
    public void RaiseDisconnected() => Disconnected?.Invoke();
}

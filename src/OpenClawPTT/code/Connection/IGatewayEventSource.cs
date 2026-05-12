using System;
using System.Text.Json;

namespace OpenClawPTT;

public interface IGatewayEventSource
{
    event Action<string, JsonElement>? EventReceived;
    event Action<string>? AgentReplyFull;
    event Action<string>? AgentReplyDelta;
    event Action<string>? AgentReplyFinal;
    event Action? AgentReplyDeltaStart;
    event Action? AgentReplyDeltaEnd;
    event Action<string>? AgentThinking;
    event Action<string, string>? AgentToolCall;
    event Action<string>? AgentReplyAudio;

    // Raise helpers — allows external callers to fire events without violating C# event-access rules
    void RaiseAgentThinking(string thinking);
    void RaiseAgentToolCall(string toolName, string arguments);
    void RaiseAgentReplyAudio(string audioText);
    void RaiseAgentReplyDeltaStart();
    void RaiseAgentReplyDeltaEnd();
    void RaiseAgentReplyFull(string text);
    void RaiseAgentReplyFinal(string text);
    void RaiseAgentReplyDelta(string chunk);
    void RaiseEventReceived(string eventName, JsonElement payload);
}

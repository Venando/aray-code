namespace OpenClawPTT.Services;

/// <summary>
/// Tracks application component status (gateway, TTS, direct LLM, agent)
/// and reflects it in the StreamShell separator bars via configurable
/// status parts.
/// </summary>
public interface IStatusService
{
    /// <summary>Update the gateway connection status displayed on the separator bar.</summary>
    void SetGatewayStatus(string label, StatusColor color);

    /// <summary>Update the TTS service status displayed on the separator bar.</summary>
    void SetTtsStatus(string label, StatusColor color);

    /// <summary>Update the STT (speech-to-text) service status displayed on the separator bar.</summary>
    void SetSttStatus(string label, StatusColor color);

    /// <summary>Update the direct LLM status displayed on the separator bar.</summary>
    void SetDirectLlmStatus(string label, StatusColor color);

    /// <summary>
    /// Record the last time the direct LLM was called.
    /// Pass null to clear the timestamp.
    /// </summary>
    void SetDirectLlmLastCalled(DateTime? timestamp);

    /// <summary>
    /// Provide an <see cref="IAgentStatusTracker"/> for rendering active agent
    /// status on the separator bar. Safe to call after construction.
    /// </summary>
    void SetAgentStatusTracker(IAgentStatusTracker tracker);

    /// <summary>
    /// Sets the conversation name displayed in the separator bars.
    /// Pass null to clear the conversation name.
    /// </summary>
    void SetConversationName(string? name);

    /// <summary>
    /// Applies per-part <see cref="DisplayPosition"/> settings from
    /// <see cref="AppConfig"/> to all status parts. Call after config load
    /// or whenever the config is updated at runtime.
    /// </summary>
    void ApplyConfigPositions(AppConfig cfg);
}
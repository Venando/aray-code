namespace ArayCode.Services;

/// <summary>
/// Identifies which application service a status update targets.
/// Replaces the per-service string-label pattern that was never rendered.
/// </summary>
public enum ServiceKind
{
    /// <summary>Gateway WebSocket connection.</summary>
    Gateway,

    /// <summary>Text-to-Speech service.</summary>
    Tts,

    /// <summary>Speech-to-Text service.</summary>
    Stt,

    /// <summary>Direct LLM endpoint probe.</summary>
    DirectLlm,
}

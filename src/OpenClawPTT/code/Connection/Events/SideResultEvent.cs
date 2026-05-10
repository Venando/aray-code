using System.Text.Json;

namespace OpenClawPTT;

/// <summary>
/// Event raised when a chat.side_result arrives from the gateway.
/// Carries the full payload — kind, question, answer text, and error flag.
/// </summary>
public record SideResultEvent(JsonElement Payload);

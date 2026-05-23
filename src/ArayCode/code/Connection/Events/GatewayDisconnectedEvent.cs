using System;

namespace ArayCode;

/// <summary>
/// Event raised when the gateway WebSocket connection is closed or lost.
/// </summary>
public record GatewayDisconnectedEvent(string Reason, Exception? Exception = null);

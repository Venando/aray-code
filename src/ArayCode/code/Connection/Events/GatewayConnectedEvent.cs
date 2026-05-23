namespace ArayCode;

/// <summary>
/// Event raised when a gateway WebSocket connection is successfully established.
/// </summary>
public record GatewayConnectedEvent(Uri Uri);

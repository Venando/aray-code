using ArayCode.Services;

namespace ArayCode;

/// <summary>
/// Handles gateway connection lifecycle events: connected and disconnected.
/// Provides logging and optional disconnection callback invocation.
/// </summary>
public class GatewayConnectionHandler :
    IEventHandler<GatewayConnectedEvent>,
    IEventHandler<GatewayDisconnectedEvent>
{
    private readonly IColorConsole _console;
    private readonly Action<CancellationToken>? _onDisconnection;

    public GatewayConnectionHandler(
        IColorConsole? console = null,
        Action<CancellationToken>? onDisconnection = null)
    {
        _console = console ?? new ColorConsole(new StreamShellHost());
        _onDisconnection = onDisconnection;
    }

    public Task HandleAsync(GatewayConnectedEvent evt)
    {
        _console.Log("gateway", $"Connected to {evt.Uri}");
        return Task.CompletedTask;
    }

    public Task HandleAsync(GatewayDisconnectedEvent evt)
    {
        _console.Log("gateway", $"Disconnected: {evt.Reason}");
        return Task.CompletedTask;
    }
}

using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace ArayCode;

/// <summary>
/// Wraps a System.Net.WebSockets.ClientWebSocket instance to implement IClientWebSocket.
/// </summary>
public sealed class ClientWebSocketAdapter : IClientWebSocket
{
    private readonly ClientWebSocket _ws;

    public ClientWebSocketAdapter()
    {
        _ws = new ClientWebSocket();
    }

    public WebSocketState State => _ws.State;
    public ClientWebSocketOptions Options => _ws.Options;

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        => _ws.ConnectAsync(uri, cancellationToken);

    public Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        => _ws.SendAsync(buffer, messageType, endOfMessage, cancellationToken);

    public Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        => _ws.ReceiveAsync(buffer, cancellationToken);

    public Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
        => _ws.CloseAsync(closeStatus, statusDescription, cancellationToken);

    public void Abort()
        => _ws.Abort();

    public void Dispose()
        => _ws.Dispose();
}

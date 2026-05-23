using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace ArayCode;

/// <summary>
/// Abstracts System.Net.WebSockets.ClientWebSocket for testability.
/// </summary>
public interface IClientWebSocket : IDisposable
{
    WebSocketState State { get; }
    ClientWebSocketOptions Options { get; }

    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);
    Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken);
    Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken);
    Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken);
    void Abort();
}

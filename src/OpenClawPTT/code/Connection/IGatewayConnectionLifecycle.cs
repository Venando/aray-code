using System.Text.Json;
using System.Threading;

namespace OpenClawPTT;

/// <summary>
/// Abstracts GatewayConnectionLifecycle for testability.
/// </summary>
public interface IGatewayConnectionLifecycle : IDisposable
{
    /// <summary>Fires after a successful connection to the gateway.</summary>
    event Action? ConnectionSucceeded;

    /// <summary>Fires when the reconnection loop begins (first attempt after disconnect).</summary>
    event Action? Reconnecting;

    /// <summary>Fires when the reconnection loop exhausts all retries without success.</summary>
    event Action? ReconnectFailed;

    bool IsConnected { get; }
    IMessageFraming? GetFraming();
    Task<JsonElement> SendRequestAsync(string method, object? parameters, CancellationToken ct, TimeSpan? timeout = null);
    Task DisconnectAsync(CancellationToken ct);
    Task ConnectAsync(CancellationToken ct);
}

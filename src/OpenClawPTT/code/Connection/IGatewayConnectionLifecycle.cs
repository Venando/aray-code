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
    bool IsConnected { get; }
    IMessageFraming? GetFraming();
    Task<JsonElement> SendRequestAsync(string method, object? parameters, CancellationToken ct, TimeSpan? timeout = null);
    Task DisconnectAsync(CancellationToken ct);
    Task ConnectAsync(CancellationToken ct);
}

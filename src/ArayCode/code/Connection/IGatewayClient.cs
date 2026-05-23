using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ArayCode;

/// <summary>
/// Abstracts the GatewayClient for testability.
/// All public members of GatewayClient are exposed through this interface.
/// </summary>
public interface IGatewayClient : IDisposable
{
    bool IsConnected { get; }
    string? SessionKey { get; }
    string? AgentId { get; }
    bool IsDisposed { get; }

    /// <summary>Fires after a successful connection to the gateway (initial or reconnection).</summary>
    event Action? ConnectionSucceeded;

    /// <summary>Fires when the reconnection loop begins after an unexpected disconnect.</summary>
    event Action? Reconnecting;

    /// <summary>Fires when the reconnection loop exhausts all retries without success.</summary>
    event Action? ReconnectFailed;

    Task ConnectAsync(CancellationToken ct);
    Task DisconnectAsync(CancellationToken ct);
    Task<JsonElement> SendTextAsync(string body, CancellationToken ct);
    Task<JsonElement> SendAudioAsync(byte[] wavBytes, CancellationToken ct);
    Task<JsonElement> SendEventAsync(string eventName, object? parameters, CancellationToken ct);
    /// <summary>Fetches recent chat history for a session. Returns null if unavailable.</summary>
    Task<List<ChatHistoryEntry>?> FetchSessionHistoryAsync(string sessionKey, int limit = 5);
    void RecreateWithConfig(AppConfig newConfig);
    IGatewayEventSource? GetEventSource();
}
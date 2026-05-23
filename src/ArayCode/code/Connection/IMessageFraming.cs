using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ArayCode;

/// <summary>
/// Abstracts MessageFraming for testability.
/// </summary>
public interface IMessageFraming
{
    string NextId();
    Task<JsonElement> SendRequestAsync(string method, object? parameters, CancellationToken ct, TimeSpan? timeout = null);
    Task<JsonElement> WaitForEventAsync(string eventName, TimeSpan timeout, CancellationToken ct);
    void RegisterEventWaiter(string eventName, TaskCompletionSource<JsonElement> tcs);
    void RemoveEventWaiter(string eventName);
    void ClearPendingRequests();
    void ClearEventWaiters();
}

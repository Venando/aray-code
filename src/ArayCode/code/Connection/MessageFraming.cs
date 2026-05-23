using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ArayCode;

/// <summary>
/// Manages message framing, request/response correlation, and one-shot event waiters.
/// Thread-safe. Used by GatewayClient to handle the WebSocket protocol.
/// </summary>
public class MessageFraming : IMessageFraming
{
    private readonly IClientWebSocket _ws;
    private readonly AppConfig _cfg;

    // pending request → response futures
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();

    // one-shot event waiters (event name → future)
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _eventWaiters = new();

    private int _idCounter;

    public MessageFraming(IClientWebSocket ws, AppConfig cfg)
    {
        _ws = ws;
        _cfg = cfg;
    }

    /// <summary>Generates the next unique request ID.</summary>
    public virtual string NextId() =>
        $"ptt-{Interlocked.Increment(ref _idCounter):D6}";

    // ─── send ───────────────────────────────────────────────────────

    /// <summary>Sends a request and waits for the response.</summary>
    public virtual async Task<JsonElement> SendRequestAsync(
        string method,
        object? parameters,
        CancellationToken ct,
        TimeSpan? timeout = null)
    {
        var id = NextId();
        var frame = new Dictionary<string, object?>
        {
            ["type"] = "req",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters
        };

        var tcs = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _pending[id] = tcs;

        var json = JsonSerializer.Serialize(frame);
        var buf = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(buf, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);

        var wait = timeout ?? TimeSpan.FromSeconds(30);
        using var timeCts = new CancellationTokenSource(wait);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeCts.Token);

        try
        {
            await using (linked.Token.Register(() => tcs.TrySetCanceled()))
                return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    // ─── event waiting ──────────────────────────────────────────────

    /// <summary>Waits for a single inbound event with the given name.</summary>
    public virtual async Task<JsonElement> WaitForEventAsync(
        string eventName,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _eventWaiters[eventName] = tcs;

        using var timeCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeCts.Token);

        await using (linked.Token.Register(() => tcs.TrySetCanceled()))
        {
            try { return await tcs.Task.ConfigureAwait(false); }
            finally { _eventWaiters.TryRemove(eventName, out _); }
        }
    }

    /// <summary>Registers a pre-built TCS as an event waiter (used to avoid race with receive loop).</summary>
    public virtual void RegisterEventWaiter(string eventName, TaskCompletionSource<JsonElement> tcs)
    {
        _eventWaiters[eventName] = tcs;
    }

    /// <summary>Removes a registered event waiter without completing it.</summary>
    public virtual void RemoveEventWaiter(string eventName)
    {
        _eventWaiters.TryRemove(eventName, out _);
    }

    // ─── cleanup ────────────────────────────────────────────────────

    /// <summary>Cancels and clears all pending request waiters.</summary>
    public virtual void ClearPendingRequests()
    {
        foreach (var kvp in _pending)
        {
            kvp.Value.TrySetCanceled();
        }
        _pending.Clear();
    }

    /// <summary>Cancels and clears all event waiters.</summary>
    public virtual void ClearEventWaiters()
    {
        foreach (var kvp in _eventWaiters)
        {
            kvp.Value.TrySetCanceled();
        }
        _eventWaiters.Clear();
    }

    /// <summary>Tries to remove and return a pending request TCS by ID.</summary>
    internal bool TryRemovePending(string id, out TaskCompletionSource<JsonElement>? tcs)
        => _pending.TryRemove(id, out tcs);

    /// <summary>Resolves a one-shot event waiter by name with the given payload.</summary>
    internal void ResolveEventWaiter(string eventName, JsonElement payload)
    {
        if (_eventWaiters.TryRemove(eventName, out var tcs))
            tcs.TrySetResult(payload);
    }
}
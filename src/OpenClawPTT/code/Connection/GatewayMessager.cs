using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OpenClawPTT.Services;

namespace OpenClawPTT;

public class GatewayMessager : IDisposable
{
    private readonly IClientWebSocket _ws;
    private readonly IGatewayEventSource _events;
    private readonly AppConfig _cfg;
    private readonly Func<MessageFraming>? _framingFactory;
    private readonly MessageFraming _framing;
    private readonly Action<CancellationToken>? _onDisconnection;
    private readonly IContentExtractor _contentExtractor;
    private readonly IColorConsole _console;
    private readonly IEventDispatcher _dispatcher;
    private readonly IBackgroundJobRunner _jobRunner;

    public IMessageFraming GetFraming() => _framing;

    public GatewayMessager(
        IClientWebSocket ws,
        IGatewayEventSource events,
        AppConfig cfg,
        Action<CancellationToken>? onDisconnection = null,
        Func<MessageFraming>? framingFactory = null,
        IContentExtractor? contentExtractor = null,
        IColorConsole? console = null,
        IEventDispatcher? dispatcher = null,
        IBackgroundJobRunner? jobRunner = null)
    {
        _ws = ws;
        _cfg = cfg;
        _events = events;
        _framingFactory = framingFactory;
        _framing = _framingFactory != null ? _framingFactory() : new MessageFraming(_ws, _cfg);
        _onDisconnection = onDisconnection;
        _contentExtractor = contentExtractor ?? new ContentExtractor();
        _console = console ?? new ColorConsole(new StreamShellHost());
        _dispatcher = dispatcher ?? new EventDispatcher(_console);
        _jobRunner = jobRunner ?? new BackgroundJobRunner(msg => _console.Log("jobrunner", msg));

        // Register default handlers
        _dispatcher.RegisterHandler<SessionMessageEvent>(
            new SessionMessageHandler(_events, _cfg, _contentExtractor, _console));
        _dispatcher.RegisterHandler<GatewayDisconnectedEvent>(
            new GatewayConnectionHandler(_console, _onDisconnection));
        _dispatcher.RegisterHandler<ModelFallbackEvent>(
            new ModelFallbackHandler(_console));
        _dispatcher.RegisterHandler<GatewayEvent>(
            new GatewayEventHandler(_console));
    }

    public async Task ReceiveLoop(CancellationToken ct)
    {
        var buf = new byte[512 * 1024];

        try
        {
            while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(buf, ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _console.Log("gateway", "Server closed connection.");
                        _dispatcher.DispatchAndForget(new GatewayDisconnectedEvent("Server closed connection."));
                        _onDisconnection?.Invoke(ct);
                        return;
                    }
                    ms.Write(buf, 0, result.Count);

                    if (result.Count == buf.Length)
                        _console.LogError("gateway", $"WARNING: fragment filled buffer ({buf.Length} bytes) — consider increasing buffer size");

                } while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(ms.ToArray());

                ProcessFrame(json);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is orchestrated from above (via _disposeCts linked token).
            // No reconnection needed — dispose path handles cleanup.
        }
        catch (WebSocketException ex)
        {
            _console.LogError("gateway", $"WebSocket error: {ex.Message}");
            _dispatcher.DispatchAndForget(new GatewayDisconnectedEvent($"WebSocket error: {ex.Message}", ex));
            _onDisconnection?.Invoke(ct);
        }
        catch (Exception ex)
        {
            _console.LogError("gateway", $"ReceiveLoop unexpected error: {ex.GetType().Name}: {ex.Message}");
            _dispatcher.DispatchAndForget(new GatewayDisconnectedEvent($"Unexpected error: {ex.Message}", ex));
            _onDisconnection?.Invoke(ct);
        }
    }

    public void ProcessFrame(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var type = root.GetProperty("type").GetString();

        switch (type)
        {
            case "res":
                HandleResponse(root);
                break;
            case "event":
                HandleEvent(root);
                break;
        }
    }

    private void HandleResponse(JsonElement root)
    {
        var id = root.GetProperty("id").GetString()!;
        if (!_framing.TryRemovePending(id, out var tcs) || tcs == null)
            return;

        var ok = root.GetProperty("ok").GetBoolean();
        if (ok)
        {
            tcs.SetResult(root.TryGetProperty("payload", out var p)
                ? p.Clone()
                : default);
        }
        else
        {
            var err = root.TryGetProperty("error", out var e)
                ? e.Clone().ToString()
                : "unknown error";
            tcs.SetException(new GatewayException(err, root.Clone()));
        }
    }

    private void HandleEvent(JsonElement root)
    {
        var name = root.GetProperty("event").GetString()!;
        var payload = root.TryGetProperty("payload", out var p) ? p.Clone() : default;

        // resolve one-shot waiter via MessageFraming (skip if _framing not yet initialized)
        if (_framing != null)
            _framing.ResolveEventWaiter(name, payload);

        // Filter messages not belonging to the active agent session
        if (payload.TryGetProperty("sessionKey", out JsonElement sessionKeyEl))
        {
            var msgSessionKey = sessionKeyEl.GetString();
            if (!AgentRegistry.IsMessageForActiveSession(msgSessionKey))
                return;
        }

        // Fire raw event received through the gateway event source
        _events.RaiseEventReceived(name, payload);

        // Route event to typed dispatchers
        switch (name)
        {
            case "session.message":
            case "agent":
            case "chat":
                _dispatcher.DispatchAndForget(new SessionMessageEvent(name, payload));
                return;

            default:
                _dispatcher.DispatchAndForget(new GatewayEvent(name, payload));
                if (name == "exec.approval.requested")
                    HandleApprovalRequest(payload);
                return;
        }
    }

    private void HandleApprovalRequest(JsonElement payload)
    {
        _console.PrintWarning("Exec approval requested");

        if (payload.TryGetProperty("description", out var d))
            _console.PrintInfo($"    {d.GetString()}");

        if (payload.TryGetProperty("command", out var cmd))
            _console.PrintInfo($"    $ {cmd.GetString()}");
        _console.PrintInfo("(auto-approving from PTT client)");

        if (payload.TryGetProperty("id", out var idEl))
        {
            var approvalId = idEl.GetString();
            _jobRunner.RunAndForget(async () =>
            {
                await _framing.SendRequestAsync("exec.approval.resolve", new Dictionary<string, object?>
                {
                    ["id"] = approvalId,
                    ["approved"] = true
                }, CancellationToken.None, TimeSpan.FromSeconds(10));
            }, $"approval-{approvalId}");
        }
    }


    public async Task<JsonElement> SendRequestAsync(
        string method,
        object? parameters,
        CancellationToken ct,
        TimeSpan? timeout = null)
        => await _framing.SendRequestAsync(method, parameters, ct, timeout);

    public void ClearFraming()
    {
        _framing?.ClearPendingRequests();
        _framing?.ClearEventWaiters();
    }

    public void Dispose()
    {
        ClearFraming();
    }

    // ─── test support ──────────────────────────────────────────────

    internal void TestProcessFrame(string json) => ProcessFrame(json);
}
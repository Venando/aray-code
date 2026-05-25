using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArayCode.Services;

namespace ArayCode;

/// <summary>
/// Owns the WebSocket connection lifecycle: connect, receive pump, reconnection, and disconnect.
/// Coordinates with MessageFraming for framing/sending and SessionMessageHandler for event dispatch.
/// </summary>
public sealed class GatewayConnectionLifecycle : IGatewayConnector, IGatewayConnectionLifecycle
{
    private readonly AppConfig _cfg;
    private readonly DeviceIdentity _deviceIdentity;
    private readonly Func<IClientWebSocket> _socketFactory;
    private readonly IColorConsole _console;
    private readonly IAgentActivityStore? _activityStore;
    private readonly IRecentMessageTracker? _tracker;

    private IClientWebSocket _ws = null!;
    private Task? _recvTask;

    private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();

    // Linked CTS for the current connection's receive loop — kept alive until
    // DisposeConnection so _recvTask can still honour cancellation.
    private CancellationTokenSource? _connectionCts;

    // ─── Dependencies ───────────────────────────────────────────────
    private readonly IGatewayEventSource _events;
    private readonly ISnapshotProcessor _snapshotProcessor;
    private GatewayMessager? _gatewayMessager;
    private GatewayReconnector _gatewayReconnector;
    private readonly IBackgroundJobRunner _jobRunner;

    public event Action? ConnectionSucceeded;

    /// <summary>Fires when the reconnection loop begins after an unexpected disconnect.</summary>
    public event Action? Reconnecting;

    /// <summary>Fires when the reconnection loop exhausts all retries without success.</summary>
    public event Action? ReconnectFailed;

    public GatewayConnectionLifecycle(AppConfig cfg, DeviceIdentity dev, IGatewayEventSource events, IColorConsole console,
        Func<IClientWebSocket>? socketFactory = null, ISnapshotProcessor? snapshotProcessor = null, IAgentActivityStore? activityStore = null,
        IRecentMessageTracker? tracker = null)
    {
        _cfg = cfg;
        _deviceIdentity = dev;
        _events = events;
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _socketFactory = socketFactory ?? (() => new ClientWebSocketAdapter());
        _snapshotProcessor = snapshotProcessor ?? new SnapshotProcessor(new ConsoleLogger(console), activityStore);
        _jobRunner = new BackgroundJobRunner(msg => _console.Log("jobrunner", msg));
        _gatewayReconnector = new GatewayReconnector(cfg, console, this, _disposeCts.Token);

        // Wire reconnector events to lifecycle relay
        _gatewayReconnector.ReconnectStarted += () => Reconnecting?.Invoke();
        // On permanent reconnect failure, fire ReconnectFailed (not Disconnected).
        // Disconnected already fired from ReceiveLoop when the connection first dropped.
        // ReconnectFailed signals the terminal state after all retries are exhausted.
        _gatewayReconnector.ReconnectFailed += () => ReconnectFailed?.Invoke();

        _activityStore = activityStore;
        _tracker = tracker;
    }

    // ─── ISender ──────────────────────────────────────────────────────

    public async Task<JsonElement> SendRequestAsync(
        string method,
        object? parameters,
        CancellationToken ct,
        TimeSpan? timeout = null)
    {
        if (_gatewayMessager == null)
            return default;

        return await _gatewayMessager.GetFraming().SendRequestAsync(method, parameters, ct, timeout);
    }

    // ─── Properties ──────────────────────────────────────────────────

    public bool IsConnected => _ws?.State == WebSocketState.Open;

    public IMessageFraming? GetFraming() => _gatewayMessager?.GetFraming();

    // ─── connect ─────────────────────────────────────────────────────

    public async Task DisconnectAsync(CancellationToken ct)
    {
        await DisconnectInternalAsync(ct);
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        await _gatewayReconnector.ReconnectLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _console.Log("gateway", "[connect] Acquired reconnect lock.");
            await DisposeConnection(ct).ConfigureAwait(false);
            _console.Log("gateway", "[connect] DisposeConnection done.");
            await ConnectWebSocketAndHandshakeAsync(ct).ConfigureAwait(false);
            _console.Log("gateway", "[connect] WebSocket handshake complete.");

            var linkCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
            try { await CompleteAuthenticationAsync(linkCts.Token).ConfigureAwait(false); }
            finally { linkCts.Dispose(); }

            _console.Log("gateway", "[connect] Authentication complete. Firing ConnectionSucceeded.");
            ConnectionSucceeded?.Invoke();
        }
        finally { _gatewayReconnector.ReconnectLock.Release(); }
    }

    private async Task ConnectWebSocketAndHandshakeAsync(CancellationToken ct)
    {
        _ws = _socketFactory();

        // Dispose any previous connection CTS before creating a new one.
        _connectionCts?.Dispose();
        _connectionCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        var linkedCt = _connectionCts.Token;
        try
        {
            _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

            // Set Origin header to satisfy gateway WebSocket handshake validation
            // (required after OpenClaw security hardening for CSWSH protection).
            // Browser clients send Origin automatically; .NET ClientWebSocket does not.
            var origin = _cfg.GatewayUrl
                .Replace("ws://", "http://", StringComparison.OrdinalIgnoreCase)
                .Replace("wss://", "https://", StringComparison.OrdinalIgnoreCase);
            _ws.Options.SetRequestHeader("Origin", origin);
            _gatewayMessager = new GatewayMessager(_ws, _events, _cfg, ct2 => _ = HandleDisconnectionAsync(ct2), console: _console, jobRunner: _jobRunner, activityStore: _activityStore, tracker: _tracker);

            await ConnectWebSocketAsync(linkedCt).ConfigureAwait(false);
            _console.Log("gateway", "[connect] WebSocket connected, starting receive loop...");

            // Register the challenge waiter BEFORE starting the receive loop so
            // we never miss a fast connect.challenge event.
            var challengeTcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            _gatewayMessager.GetFraming().RegisterEventWaiter("connect.challenge", challengeTcs);

            _recvTask = Task.Run(() => _gatewayMessager.ReceiveLoop(linkedCt), linkedCt);

            _console.Log("gateway", "[connect] Waiting for connect.challenge ...");
            var nonce = await WaitForChallengeNonceAsync(challengeTcs, linkedCt).ConfigureAwait(false);
            _console.Log("gateway", "[connect] Got challenge nonce. Sending connect request...");
            await SendConnectRequestAndValidateAsync(nonce, linkedCt).ConfigureAwait(false);
        }
        catch
        {
            // Ensure receive task gets cancelled on failure paths so it exits promptly.
            try { _connectionCts?.Cancel(); } catch { /* already disposed */ }
            throw;
        }
    }

    /// <summary>Default timeout for the TCP + TLS + WS upgrade phase.</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    private async Task ConnectWebSocketAsync(CancellationToken linkedCt)
    {
        var uri = new Uri(_cfg.GatewayUrl);
        _console.Log("gateway", $"Connecting to {uri} ...");

        // ClientWebSocket.ConnectAsync may not respect CancellationToken during
        // the TCP/TLS phase. Wrap with a timeout to prevent indefinite hangs
        // when the gateway is unreachable (e.g. firewall silently dropping packets).
        using var timeoutCts = new CancellationTokenSource(ConnectTimeout);
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(linkedCt, timeoutCts.Token);

        try
        {
            await _ws.ConnectAsync(uri, combinedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !linkedCt.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Timed out connecting to {uri.Host}:{uri.Port} after {(int)ConnectTimeout.TotalSeconds}s.");
        }

        _console.Log("gateway", "WebSocket open.");
    }

    private async Task<string> WaitForChallengeNonceAsync(TaskCompletionSource<JsonElement> challengeTcs, CancellationToken linkedCt)
    {
        if (_gatewayMessager == null)
            throw new InvalidOperationException("Gateway messager not initialized.");

        using var timeCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(linkedCt, timeCts.Token);

        await using (linked.Token.Register(() => challengeTcs.TrySetCanceled()))
        {
            try
            {
                var challenge = await challengeTcs.Task.ConfigureAwait(false);
                return challenge.GetProperty("nonce").GetString()!;
            }
            catch (OperationCanceledException) when (timeCts.IsCancellationRequested && !linkedCt.IsCancellationRequested)
            {
                throw new TimeoutException("Timed out waiting for connect.challenge after 10s.");
            }
            finally
            {
                _gatewayMessager?.GetFraming().RemoveEventWaiter("connect.challenge");
            }
        }
    }

    private async Task SendConnectRequestAndValidateAsync(string nonce, CancellationToken linkedCt)
    {
        var connectParams = BuildConnectParams(nonce);

        _console.Log("gateway", "[connect] Sending connect request...");
        JsonElement hello = await SendRequestAsync("connect", connectParams, linkedCt).ConfigureAwait(false);

        _console.Log("gateway", "[connect] Validating hello-ok...");
        ValidateHelloOk(hello);
        _console.LogOk("gateway", "Authenticated — hello-ok received.");

        ProcessHelloPayload(hello);
    }

    private Dictionary<string, object?> BuildConnectParams(string nonce)
    {
        var authToken = _cfg.AuthToken ?? "";
        var scopes = new[] { "operator.read", "operator.write", "operator.approvals", "operator.admin" };
        var platform = DeviceIdentity.GetPlatform().ToLowerInvariant();
        var deviceFamily = "desktop";
        var clientId = "cli";
        var mode = "cli";
        var signedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var sigPayload = _deviceIdentity.BuildV3Payload(platform, deviceFamily, clientId, mode, "operator", scopes, signedAt, authToken, nonce);

        var redactedPayload = string.IsNullOrEmpty(authToken)
            ? sigPayload
            : sigPayload.Replace(authToken, "***REDACTED***");
        _console.Log("gateway", $"Signature payload: {redactedPayload}", LogLevel.Verbose);

        var signature = _deviceIdentity.Sign(sigPayload);

        var authDict = new Dictionary<string, object> { ["token"] = authToken };
        if (!string.IsNullOrEmpty(_cfg.DeviceToken))
            authDict["deviceToken"] = _cfg.DeviceToken;

        return new Dictionary<string, object?>
        {
            ["minProtocol"] = 3,
            ["maxProtocol"] = 4,
            ["client"] = new Dictionary<string, object>
            {
                ["id"] = clientId,
                ["version"] = _cfg.ClientVersion,
                ["platform"] = platform,
                ["mode"] = mode,
                ["deviceFamily"] = deviceFamily,
                ["displayName"] = "ArayCode"
            },
            ["role"] = "operator",
            ["scopes"] = scopes,
            ["caps"] = new[] { "streaming", "stream.text", "agent.stream", "text.stream" },
            ["commands"] = Array.Empty<string>(),
            ["permissions"] = new Dictionary<string, object>(),
            ["auth"] = authDict,
            ["locale"] = _cfg.Locale,
            ["userAgent"] = $"aray-code-cli/{_cfg.ClientVersion}",
            ["device"] = new Dictionary<string, object>
            {
                ["id"] = _deviceIdentity.DeviceId,
                ["publicKey"] = _deviceIdentity.PublicKeyBase64,
                ["signature"] = signature,
                ["signedAt"] = signedAt,
                ["nonce"] = nonce
            }
        };
    }

    private void ValidateHelloOk(JsonElement hello)
    {
        var helloType = hello.TryGetProperty("type", out var htEl) ? htEl.GetString() : null;
        if (helloType != "hello-ok")
            throw new Exception($"Handshake rejected: {hello}");

        if (hello.TryGetProperty("error", out var err))
            throw new Exception($"Server returned hello-ok with error: {err}");

        if (hello.TryGetProperty("protocol", out var protoEl)
            && protoEl.TryGetInt32(out var protocol))
        {
            if (protocol < 3 || protocol > 4)
                throw new Exception($"Unsupported protocol version negotiated: {protocol} (expected 3-4)");
            _console.Log("gateway", $"Protocol version negotiated: {protocol}", LogLevel.Verbose);
        }
        else
        {
            _console.Log("gateway", "Protocol version field missing in hello-ok", LogLevel.Warning);
        }
    }

    private void ProcessHelloPayload(JsonElement hello)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        string prettyHello = JsonSerializer.Serialize(hello, options);
        string extraPretty = Regex.Replace(prettyHello, "(?m)^(  )+", m => new string(' ', m.Length * 2));
        var lines = $"--- SERVER HELLO PAYLOAD ---\n{extraPretty}\n----------------------------".Split('\n');
        foreach (var line in lines) _console.Log("ws", line, LogLevel.Verbose);

        PersistDeviceTokenIfIssued(hello);

        // Save last active agent before snapshot resets agents
        var previousAgentId = AgentRegistry.ActiveAgentId;
        if (previousAgentId != null)
            _cfg.LastActiveAgentId = previousAgentId;

        _snapshotProcessor.ProcessSnapshot(hello);

        // Restore last active agent after snapshot
        var restoreId = _cfg.LastActiveAgentId;
        if (restoreId != null && AgentRegistry.Agents.Any(a =>
            a.AgentId.Equals(restoreId, StringComparison.OrdinalIgnoreCase)))
        {
            AgentRegistry.SetActiveAgent(restoreId);
        }
    }

    private void PersistDeviceTokenIfIssued(JsonElement hello)
    {
        if (hello.TryGetProperty("auth", out var authEl)
            && authEl.TryGetProperty("deviceToken", out var dtEl))
        {
            _cfg.DeviceToken = dtEl.GetString();
            new ConfigurationService().Save(_cfg);
        }
    }

    private async Task CompleteAuthenticationAsync(CancellationToken linkedCt)
    {
        await SubscribeToSessionAsync(linkedCt);

        // When the user switches active agent via StreamShell command, resubscribe
        AgentRegistry.ActiveSessionChanged += OnActiveSessionChanged;
    }

    private void OnActiveSessionChanged(string? newSessionKey)
    {
        if (newSessionKey == null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var subscribeParams = new Dictionary<string, object?>
                {
                    ["sessionKey"] = newSessionKey
                };
                await SendRequestAsync("sessions.subscribe", subscribeParams, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _console.LogError("gateway", $"Failed to subscribe to new session: {ex.Message}");
            }
        });
    }

    private async Task SubscribeToSessionAsync(CancellationToken linkedCt)
    {
        var sessionKey = AgentRegistry.ActiveSessionKey ?? "main";
        var subscribeParams = new Dictionary<string, object?>
        {
            ["sessionKey"] = sessionKey
        };

        await SendRequestAsync("sessions.subscribe", subscribeParams, linkedCt).ConfigureAwait(false);
    }

    // ─── connection resilience ───────────────────────────────────────

    private async Task DisconnectInternalAsync(CancellationToken ct)
    {
        if (_ws.State == WebSocketState.Open)
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "disconnect", ct).ConfigureAwait(false);
            }
            catch { /* ignore */ }
        }

        _gatewayMessager?.ClearFraming();
    }

    private async Task HandleDisconnectionAsync(CancellationToken ct)
    {
        try
        {
            // Save the active agent before disconnect so it can be restored on reconnect
            if (AgentRegistry.ActiveAgentId != null)
                _cfg.LastActiveAgentId = AgentRegistry.ActiveAgentId;

            await DisposeConnection(ct).ConfigureAwait(false);
            _ = _gatewayReconnector.ScheduleReconnectAsync(ct);
        }
        catch (Exception ex)
        {
            _console.LogError("gateway", $"Error during disconnection handling: {ex.Message}");
        }
    }

    // ─── dispose connection ───────────────────────────────────────────

    private async Task DisposeConnection(CancellationToken ct)
    {
        // Cancel the connection-scoped CTS so the receive loop exits promptly.
        try { _connectionCts?.Cancel(); } catch { /* already disposed */ }

        // Give the receive loop a short grace period to exit after cancellation.
        if (_recvTask != null)
        {
            try { await _recvTask.WaitAsync(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false); }
            catch { /* best effort — we'll abort the socket below */ }
        }

        _gatewayMessager?.Dispose();
        _gatewayMessager = null;

        if (_ws == null) return;

        try
        {
            if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived)
            {
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                try
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", linkedCts.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    _ws.Abort();
                }
            }
        }
        finally
        {
            _ws.Dispose();
            _ws = null!;
            _recvTask = null;
        }
    }

    // ─── IDisposable ─────────────────────────────────────────────────

    public void Dispose()
    {
        try { _disposeCts.Cancel(); } catch (ObjectDisposedException) { /* already disposed */ }

        // Cancel connection-scoped CTS so receive loop exits.
        try { _connectionCts?.Cancel(); } catch { }

        _recvTask?.Wait(TimeSpan.FromSeconds(3));

        if (_ws != null && _ws.State == WebSocketState.Open)
        {
            try { _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).Wait(3000); }
            catch { /* best effort */ }
        }
        _ws?.Dispose();

        // Clean up any in-flight send/request TCS to prevent tasks hanging forever
        _gatewayMessager?.Dispose();
        _gatewayMessager = null;

        _gatewayReconnector?.Dispose();
        _connectionCts?.Dispose();
        try { _disposeCts.Dispose(); } catch (ObjectDisposedException) { /* already disposed */ }
    }
}

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OpenClawPTT.Services;

namespace OpenClawPTT;

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
    private readonly IAgentStatusTracker? _agentStatusTracker;

    private IClientWebSocket _ws = null!;
    private CancellationTokenSource? _tickCts;
    private Task? _recvTask;

    private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();

    // ─── Dependencies ───────────────────────────────────────────────
    private readonly IGatewayEventSource _events;
    private readonly ISnapshotProcessor _snapshotProcessor;
    private GatewayMessager? _gatewayMessager;
    private GatewayReconnector _gatewayReconnector;
    private readonly IBackgroundJobRunner _jobRunner;

    public event Action? ConnectionSucceeded;

    public GatewayConnectionLifecycle(AppConfig cfg, DeviceIdentity dev, IGatewayEventSource events, IColorConsole console,
        Func<IClientWebSocket>? socketFactory = null, ISnapshotProcessor? snapshotProcessor = null, IAgentStatusTracker? agentStatusTracker = null)
    {
        _cfg = cfg;
        _deviceIdentity = dev;
        _events = events;
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _socketFactory = socketFactory ?? (() => new ClientWebSocketAdapter());
        _snapshotProcessor = snapshotProcessor ?? new SnapshotProcessor(new ConsoleLogger(console), agentStatusTracker);
        _jobRunner = new BackgroundJobRunner(msg => _console.Log("jobrunner", msg));
        _gatewayReconnector = new GatewayReconnector(cfg, console, this, _disposeCts.Token);
        _agentStatusTracker = agentStatusTracker;
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
        await _gatewayReconnector.ReconnectLock.WaitAsync(ct);
        try
        {
            await DisposeConnection(ct);
            var tickMs = await ConnectWebSocketAndHandshakeAsync(ct);

            var linkCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
            try { await CompleteAuthenticationAsync(tickMs, linkCts.Token); }
            finally { linkCts.Dispose(); }

            ConnectionSucceeded?.Invoke();
        }
        finally { _gatewayReconnector.ReconnectLock.Release(); }
    }

    private async Task<int> ConnectWebSocketAndHandshakeAsync(CancellationToken ct)
    {
        _ws = _socketFactory();

        var linkCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        var linkedCt = linkCts.Token;
        try
        {
            _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
            _gatewayMessager = new GatewayMessager(_ws, _events, _cfg, ct => _ = HandleDisconnectionAsync(ct), console: _console, jobRunner: _jobRunner, agentStatusTracker: _agentStatusTracker);

            await ConnectWebSocketAsync(linkedCt);
            _recvTask = Task.Run(() => _gatewayMessager.ReceiveLoop(linkedCt), linkedCt);

            var nonce = await WaitForChallengeNonceAsync(linkedCt);
            return await SendConnectRequestAndValidateAsync(nonce, linkedCt);
        }
        finally { linkCts.Dispose(); }
    }

    private async Task ConnectWebSocketAsync(CancellationToken linkedCt)
    {
        var uri = new Uri(_cfg.GatewayUrl);
        _console.Log("gateway", $"Connecting to {uri} ...");
        await _ws.ConnectAsync(uri, linkedCt);
        _console.Log("gateway", "WebSocket open.");
    }

    private async Task<string> WaitForChallengeNonceAsync(CancellationToken linkedCt)
    {
        _console.Log("gateway", "Waiting for connect.challenge ...");
        if (_gatewayMessager == null)
            throw new InvalidOperationException("Gateway messager not initialized.");
        var challenge = await _gatewayMessager.GetFraming().WaitForEventAsync("connect.challenge", TimeSpan.FromSeconds(10), linkedCt);
        return challenge.GetProperty("nonce").GetString()!;
    }

    private async Task<int> SendConnectRequestAndValidateAsync(string nonce, CancellationToken linkedCt)
    {
        var connectParams = BuildConnectParams(nonce);

        _console.Log("gateway", "Sending connect ...");
        JsonElement hello = await SendRequestAsync("connect", connectParams, linkedCt);

        ValidateHelloOk(hello);
        _console.LogOk("gateway", "Authenticated — hello-ok received.");

        return ProcessHelloPayload(hello);
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
            ["maxProtocol"] = 3,
            ["client"] = new Dictionary<string, object>
            {
                ["id"] = clientId,
                ["version"] = _cfg.ClientVersion,
                ["platform"] = platform,
                ["mode"] = mode,
                ["deviceFamily"] = deviceFamily
            },
            ["role"] = "operator",
            ["scopes"] = scopes,
            ["caps"] = new[] { "streaming", "stream.text", "agent.stream", "text.stream" },
            ["commands"] = Array.Empty<string>(),
            ["permissions"] = new Dictionary<string, object>(),
            ["auth"] = authDict,
            ["locale"] = _cfg.Locale,
            ["userAgent"] = $"openclaw-ptt-cli/{_cfg.ClientVersion}",
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
    }

    private int ProcessHelloPayload(JsonElement hello)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        string prettyHello = JsonSerializer.Serialize(hello, options);
        string extraPretty = Regex.Replace(prettyHello, "(?m)^(  )+", m => new string(' ', m.Length * 2));
        var lines = $"--- SERVER HELLO PAYLOAD ---\n{extraPretty}\n----------------------------".Split('\n');
        foreach (var line in lines) _console.Log("ws", line, LogLevel.Verbose);

        PersistDeviceTokenIfIssued(hello);
        var tickMs = ExtractTickIntervalMs(hello);

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

        return tickMs;
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

    private async Task CompleteAuthenticationAsync(int tickMs, CancellationToken linkedCt)
    {
        StartKeepalive(tickMs, linkedCt);
        _console.Log("gateway", $"Keepalive every {tickMs}ms.");

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
                await SendRequestAsync("sessions.subscribe", subscribeParams, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _console.LogError("gateway", $"Failed to subscribe to new session: {ex.Message}");
            }
        });
    }

    private int ExtractTickIntervalMs(JsonElement hello)
    {
        if (hello.TryGetProperty("policy", out var pol)
            && pol.TryGetProperty("tickIntervalMs", out var tEl))
            return tEl.GetInt32();
        return 15_000;
    }

    private async Task SubscribeToSessionAsync(CancellationToken linkedCt)
    {
        var sessionKey = AgentRegistry.ActiveSessionKey ?? "main";
        var subscribeParams = new Dictionary<string, object?>
        {
            ["sessionKey"] = sessionKey
        };

        await SendRequestAsync("sessions.subscribe", subscribeParams, linkedCt);
    }

    // ─── keepalive ──────────────────────────────────────────────────

    private void StartKeepalive(int intervalMs, CancellationToken ct)
    {
        _tickCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tickCt = _tickCts.Token;

        _ = Task.Run(async () =>
        {
            while (!tickCt.IsCancellationRequested)
            {
                await Task.Delay(intervalMs, tickCt);
                try
                {
                    if (_ws.State == WebSocketState.Open)
                        await SendRequestAsync("tick", null, tickCt, TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException) { break; }
                catch { /* swallow tick failures */ }
            }
        }, tickCt);
    }

    // ─── connection resilience ───────────────────────────────────────

    private async Task DisconnectInternalAsync(CancellationToken ct)
    {
        _tickCts?.Cancel();
        _tickCts?.Dispose();
        _tickCts = null;

        if (_ws.State == WebSocketState.Open)
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "disconnect", ct);
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

            await DisposeConnection(ct);
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
        if (_tickCts != null)
        {
            await _tickCts.CancelAsync();
            _tickCts.Dispose();
            _tickCts = null;
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
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", linkedCts.Token);
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
        }
    }

    // ─── IDisposable ─────────────────────────────────────────────────

    public void Dispose()
    {
        try { _disposeCts.Cancel(); } catch (ObjectDisposedException) { /* already disposed */ }

        _tickCts?.Cancel();
        _tickCts?.Dispose();
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
        try { _disposeCts.Dispose(); } catch (ObjectDisposedException) { /* already disposed */ }
    }
}

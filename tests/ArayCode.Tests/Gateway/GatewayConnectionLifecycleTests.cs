using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json;
using Moq;
using ArayCode.Services;
using Xunit;

namespace ArayCode.Tests.Gateway;

public class GatewayConnectionLifecycleTests : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly DeviceIdentity _dev;
    private readonly GatewayEventSource _events;
    private readonly Mock<IClientWebSocket> _mockWs;
    private readonly Mock<IColorConsole> _mockConsole;

    public GatewayConnectionLifecycleTests()
    {
        _cfg = new AppConfig
        {
            CustomDataDir = Path.GetTempPath(),
            GatewayUrl = "wss://127.0.0.1:9999/test",
            AuthToken = "test-token"
        };
        _dev = new DeviceIdentity(_cfg.DataDir);
        _dev.EnsureKeypair();
        _events = new GatewayEventSource();
        _mockWs = new Mock<IClientWebSocket>();
        _mockWs.Setup(x => x.State).Returns(WebSocketState.Open);
        _mockConsole = new Mock<IColorConsole>();
    }

    private GatewayConnectionLifecycle CreateWithMockSocket()
    {
        return new GatewayConnectionLifecycle(_cfg, _dev, _events, _mockConsole.Object, () => _mockWs.Object);
    }

    private GatewayConnectionLifecycle CreateWithMockSocketAndSnapshotProcessor(ISnapshotProcessor? processor = null)
    {
        processor ??= new Mock<ISnapshotProcessor>().Object;
        return new GatewayConnectionLifecycle(_cfg, _dev, _events, _mockConsole.Object, () => _mockWs.Object, processor);
    }

    // ─── reflection helpers ─────────────────────────────────────────

    private static T InvokePrivate<T>(object target, string name, params object[] args)
    {
        var method = target.GetType().GetMethod(name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)!;
        return (T)method.Invoke(target, args)!;
    }

    private static void InvokeVoid(object target, string name, params object[] args)
    {
        var method = target.GetType().GetMethod(name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)!;
        method.Invoke(target, args);
    }

    // ─── construction ────────────────────────────────────────────────

    [Fact]
    public void Construct_WithMockSocketFactory_DoesNotThrow()
    {
        var exception = Record.Exception(() => CreateWithMockSocket());
        Assert.Null(exception);
    }



    [Fact]
    public void GetFraming_BeforeConnect_ReturnsNull()
    {
        var lifecycle = CreateWithMockSocket();
        Assert.Null(lifecycle.GetFraming());
        lifecycle.Dispose();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var lifecycle = CreateWithMockSocket();
        lifecycle.Dispose();
        var exception = Record.Exception(() => lifecycle.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_AfterConstruction_DoesNotThrow()
    {
        var lifecycle = CreateWithMockSocket();
        var exception = Record.Exception(() => lifecycle.Dispose());
        Assert.Null(exception);
    }

    // ─── BuildConnectParams ─────────────────────────────────────────

    [Fact]
    public void BuildConnectParams_IncludesDeviceSignatureAndNonce()
    {
        var lifecycle = CreateWithMockSocket();
        var nonce = "nonce-for-signature";

        var result = InvokePrivate<Dictionary<string, object?>>(lifecycle, "BuildConnectParams", nonce);
        var device = (Dictionary<string, object>)result["device"]!;

        Assert.Equal(_dev.DeviceId, device["id"]);
        Assert.Equal(_dev.PublicKeyBase64, device["publicKey"]);
        Assert.Equal(nonce, device["nonce"]);
        Assert.NotNull(device["signature"]);
        Assert.NotNull(device["signedAt"]);

        lifecycle.Dispose();
    }

    [Fact]
    public void BuildConnectParams_IncludesAuthToken()
    {
        var lifecycle = CreateWithMockSocket();

        var result = InvokePrivate<Dictionary<string, object?>>(lifecycle, "BuildConnectParams", "nonce");
        var auth = (Dictionary<string, object>)result["auth"]!;

        Assert.Equal(_cfg.AuthToken, auth["token"]);
        lifecycle.Dispose();
    }

    [Fact]
    public void BuildConnectParams_IncludesDeviceTokenWhenSet()
    {
        _cfg.DeviceToken = "my-device-token";
        var lifecycle = CreateWithMockSocket();

        var result = InvokePrivate<Dictionary<string, object?>>(lifecycle, "BuildConnectParams", "nonce");
        var auth = (Dictionary<string, object>)result["auth"]!;

        Assert.Equal(_cfg.DeviceToken, auth["deviceToken"]);
        lifecycle.Dispose();
    }

    [Fact]
    public void BuildConnectParams_ClientHasCorrectStructure()
    {
        var lifecycle = CreateWithMockSocket();

        var result = InvokePrivate<Dictionary<string, object?>>(lifecycle, "BuildConnectParams", "nonce");
        var client = (Dictionary<string, object>)result["client"]!;

        Assert.Equal("cli", client["id"]);
        Assert.Equal(_cfg.ClientVersion, client["version"]);
        Assert.Equal("cli", client["mode"]);
        Assert.Equal("desktop", client["deviceFamily"]);
        Assert.NotNull(client["platform"]);

        lifecycle.Dispose();
    }

    // ─── ValidateHelloOk ─────────────────────────────────────────────

    [Fact]
    public void ValidateHelloOk_ValidHelloOk_DoesNotThrow()
    {
        var lifecycle = CreateWithMockSocket();
        var json = JsonDocument.Parse(/* lang=json */ """
            {"type":"hello-ok"}
            """).RootElement;

        var exception = Record.Exception(() => InvokeVoid(lifecycle, "ValidateHelloOk", json));
        Assert.Null(exception);

        lifecycle.Dispose();
    }

    [Fact]
    public void ValidateHelloOk_TypeNotHelloOk_Throws()
    {
        var lifecycle = CreateWithMockSocket();
        var json = JsonDocument.Parse(/* lang=json */ """
            {"type":"error","message":"bad"}
            """).RootElement;

        var exception = Record.Exception(() => InvokeVoid(lifecycle, "ValidateHelloOk", json));
        Assert.NotNull(exception);
        Assert.Contains("Handshake rejected", exception.InnerException?.Message ?? exception.Message);

        lifecycle.Dispose();
    }

    [Fact]
    public void ValidateHelloOk_HelloOkWithErrorField_Throws()
    {
        var lifecycle = CreateWithMockSocket();
        var json = JsonDocument.Parse(/* lang=json */ """
            {"type":"hello-ok","error":"something went wrong"}
            """).RootElement;

        var exception = Record.Exception(() => InvokeVoid(lifecycle, "ValidateHelloOk", json));
        Assert.NotNull(exception);
        Assert.Contains("Server returned hello-ok with error", exception.InnerException?.Message ?? exception.Message);

        lifecycle.Dispose();
    }

    [Fact]
    public void ValidateHelloOk_MissingType_Throws()
    {
        var lifecycle = CreateWithMockSocket();
        var json = JsonDocument.Parse(/* lang=json */ """
            {}
            """).RootElement;

        var exception = Record.Exception(() => InvokeVoid(lifecycle, "ValidateHelloOk", json));
        Assert.NotNull(exception);

        lifecycle.Dispose();
    }

    // ─── PersistDeviceTokenIfIssued ─────────────────────────────────

    [Fact]
    public void PersistDeviceTokenIfIssued_WithDeviceToken_SetsConfig()
    {
        _cfg.DeviceToken = null;
        var lifecycle = CreateWithMockSocket();

        var json = JsonDocument.Parse(/* lang=json */ """
            {"auth":{"deviceToken":"issued-token-xyz"}}
            """).RootElement;

        InvokeVoid(lifecycle, "PersistDeviceTokenIfIssued", json);

        Assert.Equal("issued-token-xyz", _cfg.DeviceToken);
        lifecycle.Dispose();
    }

    [Fact]
    public void PersistDeviceTokenIfIssued_NoDeviceToken_DoesNotThrow()
    {
        var lifecycle = CreateWithMockSocket();
        var json = JsonDocument.Parse(/* lang=json */ """
            {"auth":{"token":"some-token"}}
            """).RootElement;

        var exception = Record.Exception(() => InvokeVoid(lifecycle, "PersistDeviceTokenIfIssued", json));
        Assert.Null(exception);

        lifecycle.Dispose();
    }

    [Fact]
    public void PersistDeviceTokenIfIssued_NoAuth_DoesNotThrow()
    {
        var lifecycle = CreateWithMockSocket();
        var json = JsonDocument.Parse(/* lang=json */ """
            {}
            """).RootElement;

        var exception = Record.Exception(() => InvokeVoid(lifecycle, "PersistDeviceTokenIfIssued", json));
        Assert.Null(exception);

        lifecycle.Dispose();
    }

    // ─── ProcessHelloPayload ─────────────────────────────────────────

    [Fact]
    public void ProcessHelloPayload_WithPolicy_ProcessesSnapshotAndPersistsToken()
    {
        var mockSnapshotProcessor = new Mock<ISnapshotProcessor>();
        var lifecycle = CreateWithMockSocketAndSnapshotProcessor(mockSnapshotProcessor.Object);

        var json = JsonDocument.Parse(/* lang=json */ """
            {
              "type": "hello-ok",
              "policy": {"tickIntervalMs": 20000},
              "auth": {"deviceToken": "tok"},
              "snapshot": {"health":{"agents":[{"agentId":"test","name":"Test Agent","isDefault":true}]}}
            }
            """).RootElement;

        InvokeVoid(lifecycle, "ProcessHelloPayload", json);

        Assert.Equal("tok", _cfg.DeviceToken);
        mockSnapshotProcessor.Verify(x => x.ProcessSnapshot(json), Times.Once);
        lifecycle.Dispose();
    }

    [Fact]
    public void ProcessHelloPayload_WithoutOptionalFields_DoesNotThrow()
    {
        var mockSnapshotProcessor = new Mock<ISnapshotProcessor>();
        var lifecycle = CreateWithMockSocketAndSnapshotProcessor(mockSnapshotProcessor.Object);

        var json = JsonDocument.Parse(/* lang=json */ """
            {"type":"hello-ok"}
            """).RootElement;

        var exception = Record.Exception(() => InvokeVoid(lifecycle, "ProcessHelloPayload", json));
        Assert.Null(exception);
        mockSnapshotProcessor.Verify(x => x.ProcessSnapshot(json), Times.Once);
        lifecycle.Dispose();
    }

    public void Dispose() { }
}

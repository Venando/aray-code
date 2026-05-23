using System.Net.WebSockets;
using System.Text.Json;
using Moq;
using ArayCode;
using ArayCode.Services;
using Xunit;

namespace ArayCode.Tests.Gateway;

public class GatewayReconnectorTests : IDisposable
{
    private readonly AppConfig _cfg;
    private readonly Mock<IGatewayConnector> _mockConnector;
    private readonly Mock<IColorConsole> _mockConsole;
    private readonly CancellationTokenSource _cts;
    private readonly GatewayReconnector _reconnector;

    public GatewayReconnectorTests()
    {
        _cfg = new AppConfig
        {
            CustomDataDir = Path.GetTempPath(),
            ReconnectDelaySeconds = 0 // fast reconnect for tests
        };
        _mockConnector = new Mock<IGatewayConnector>();
        _mockConsole = new Mock<IColorConsole>();
        _cts = new CancellationTokenSource();
        _reconnector = new GatewayReconnector(_cfg, _mockConsole.Object, _mockConnector.Object, _cts.Token);
    }

    private GatewayReconnector CreateReconnector(AppConfig? cfg = null, IColorConsole? console = null, IGatewayConnector? connector = null, CancellationToken ct = default)
    {
        return new GatewayReconnector(
            cfg ?? _cfg,
            console ?? _mockConsole.Object,
            connector ?? _mockConnector.Object,
            ct == default ? _cts.Token : ct);
    }

    [Fact]
    public void ReconnectLock_IsInitiallyAvailable()
    {
        var reconn = CreateReconnector();
        Assert.True(reconn.ReconnectLock.Wait(0));
        reconn.ReconnectLock.Release();
    }

    [Fact]
    public async Task ScheduleReconnectAsync_AcquiresLock()
    {
        var reconn = CreateReconnector();

        var task = reconn.ScheduleReconnectAsync(CancellationToken.None);

        // Lock should be acquired (wait 500ms)
        Assert.True(reconn.ReconnectLock.Wait(TimeSpan.FromMilliseconds(500)));
        reconn.ReconnectLock.Release();
        _cts.Cancel();

        try { await task.WaitAsync(TimeSpan.FromSeconds(1)); } catch { /* ignore */ }
    }

    [Fact]
    public async Task ScheduleReconnectAsync_WhenCancelled_ReturnsImmediately()
    {
        using var localCts = new CancellationTokenSource();
        localCts.Cancel();
        var reconn = CreateReconnector(ct: localCts.Token);

        var exception = await Record.ExceptionAsync(() => reconn.ScheduleReconnectAsync(localCts.Token));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ScheduleReconnectAsync_WhenCalledTwice_OnlyStartsOneLoop()
    {
        // Arrange: connector hangs until released (so _isReconnecting stays true)
        var hangSignal = new SemaphoreSlim(0, 1);
        var callCount = 0;
        _mockConnector.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .Callback(() => callCount++)
            .Returns(async () => {
                // Wait until the test releases us
                await hangSignal.WaitAsync();
            });

        // Act: schedule reconnect twice in quick succession
        var firstTask = _reconnector.ScheduleReconnectAsync(CancellationToken.None);
        // Give first loop time to enter the hang
        await Task.Delay(50);
        var secondTask = _reconnector.ScheduleReconnectAsync(CancellationToken.None);

        // Assert: ConnectAsync should only be called once (second call exits early via _isReconnecting)
        Assert.Equal(1, callCount);

        // Cleanup: release the hang
        hangSignal.Release();
        _cts.Cancel();
        try { await firstTask; } catch { /* ignore */ }
    }

    [Fact]
    public async Task Dispose_CalledTwice_DoesNotThrow()
    {
        var reconn = CreateReconnector();
        _cts.Cancel();
        try { await reconn.ScheduleReconnectAsync(CancellationToken.None).WaitAsync(TimeSpan.FromMilliseconds(500)); } catch { /* ignore */ }

        reconn.Dispose();
        var exception = Record.Exception(() => reconn.Dispose());

        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_WaitsForReconnectTask()
    {
        var reconn = CreateReconnector();

        // Should not throw — Dispose waits up to 5 seconds
        var exception = Record.Exception(() => reconn.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public async Task ReconnectLoop_OnAuthError_LogsSuggestionAndDoesNotRetry()
    {
        // Arrange: connector fails with auth error
        var callCount = 0;
        var connectCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _mockConnector.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .Callback(() => { callCount++; connectCalled.TrySetResult(); })
            .ThrowsAsync(new GatewayException("Authentication failed: invalid token",
                JsonDocument.Parse(/* lang=json */ """
                    {"error":{"code":"UNAUTHORIZED","details":{"code":"UNAUTHORIZED","recommendedNextStep":"Generate a new token and set it in config."}}}
                """).RootElement));

        _mockConsole.Setup(x => x.Log(It.IsAny<string>(), It.IsAny<string>()));
        _mockConsole.Setup(x => x.LogError(It.IsAny<string>(), It.IsAny<string>()));

        // Act: start reconnection
        var reconnectTask = _reconnector.ScheduleReconnectAsync(CancellationToken.None);

        // Wait for the connect attempt to complete
        await connectCalled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Assert: should have attempted only once, logged error + suggestion, and stopped
        Assert.Equal(1, callCount);
        _mockConsole.Verify(x => x.LogError("gateway",
            It.Is<string>(s => s.Contains("Authentication failed"))), Times.AtLeastOnce);
        _mockConsole.Verify(x => x.Log("gateway",
            It.Is<string>(s => s.Contains("Suggested actions"))), Times.AtLeastOnce);
        _mockConsole.Verify(x => x.Log("gateway",
            It.Is<string>(s => s.Contains("Verify the gateway token"))), Times.AtLeastOnce);

        _cts.Cancel();
        try { await reconnectTask.WaitAsync(TimeSpan.FromSeconds(1)); } catch { /* ignore */ }
    }

    [Fact]
    public async Task ReconnectLoop_OnNetworkError_Retries()
    {
        // Arrange: connector fails with network error first, then succeeds
        var attemptCount = 0;
        var secondAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _mockConnector.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                attemptCount++;
                if (attemptCount == 2) secondAttempt.TrySetResult();
            })
            .Returns(() =>
            {
                if (attemptCount == 1)
                    throw new WebSocketException("Connection refused");
                return Task.CompletedTask; // second attempt succeeds
            });

        _mockConsole.Setup(x => x.Log(It.IsAny<string>(), It.IsAny<string>()));
        _mockConsole.Setup(x => x.LogOk(It.IsAny<string>(), It.IsAny<string>()));

        // Act: start reconnection
        var reconnectTask = _reconnector.ScheduleReconnectAsync(CancellationToken.None);

        // Wait for the retry attempt (second call to ConnectAsync)
        await secondAttempt.Task.WaitAsync(TimeSpan.FromSeconds(2));
        // Small yield for async logging to complete
        await Task.Delay(50);

        // Assert: should have attempted twice (fail once, succeed once)
        Assert.Equal(2, attemptCount);
        _mockConsole.Verify(x => x.LogOk("gateway", "Reconnected successfully."), Times.AtLeastOnce);

        _cts.Cancel();
        try { await reconnectTask.WaitAsync(TimeSpan.FromSeconds(1)); } catch { /* ignore */ }
    }

    [Fact]
    public async Task ReconnectLoop_OnDeviceTokenMismatch_LogsSuggestionAndDoesNotRetry()
    {
        var callCount = 0;
        var connectCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _mockConnector.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .Callback(() => { callCount++; connectCalled.TrySetResult(); })
            .ThrowsAsync(new GatewayException("Device token mismatch",
                JsonDocument.Parse(/* lang=json */ """
                    {"error":{"code":"AUTH_DEVICE_TOKEN_MISMATCH","details":{"code":"AUTH_DEVICE_TOKEN_MISMATCH","recommendedNextStep":"Run: openclaw device token rotate"}}}
                """).RootElement));

        _mockConsole.Setup(x => x.Log(It.IsAny<string>(), It.IsAny<string>()));
        _mockConsole.Setup(x => x.LogError(It.IsAny<string>(), It.IsAny<string>()));

        var reconnectTask = _reconnector.ScheduleReconnectAsync(CancellationToken.None);
        await connectCalled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, callCount);
        _mockConsole.Verify(x => x.Log("gateway",
            It.Is<string>(s => s.Contains("openclaw device token rotate"))), Times.AtLeastOnce);

        _cts.Cancel();
        try { await reconnectTask.WaitAsync(TimeSpan.FromSeconds(1)); } catch { /* ignore */ }
    }

    [Fact]
    public async Task ReconnectLoop_OnFatalError_LogsFatalMessageAndDoesNotRetry()
    {
        // Arrange: connector fails with a non-GatewayException that's not network-related
        var callCount = 0;
        var connectCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _mockConnector.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .Callback(() => { callCount++; connectCalled.TrySetResult(); })
            .ThrowsAsync(new InvalidOperationException("Something went terribly wrong"));

        _mockConsole.Setup(x => x.Log(It.IsAny<string>(), It.IsAny<string>()));
        _mockConsole.Setup(x => x.LogError(It.IsAny<string>(), It.IsAny<string>()));

        // Act: start reconnection
        var reconnectTask = _reconnector.ScheduleReconnectAsync(CancellationToken.None);

        // Wait for the connect attempt to complete
        await connectCalled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Assert: only attempted once, logged fatal message
        Assert.Equal(1, callCount);
        _mockConsole.Verify(x => x.LogError("gateway",
            It.Is<string>(s => s.Contains("Fatal error"))), Times.AtLeastOnce);

        _cts.Cancel();
        try { await reconnectTask.WaitAsync(TimeSpan.FromSeconds(1)); } catch { /* ignore */ }
    }

    [Fact]
    public async Task ReconnectLoop_OnPairingRequired_LogsSuggestionAndDoesNotRetry()
    {
        var callCount = 0;
        var connectCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _mockConnector.Setup(x => x.ConnectAsync(It.IsAny<CancellationToken>()))
            .Callback(() => { callCount++; connectCalled.TrySetResult(); })
            .ThrowsAsync(new GatewayException("Pairing required",
                JsonDocument.Parse(/* lang=json */ """
                    {"error":{"code":"PAIRING_REQUIRED","details":{"code":"PAIRING_REQUIRED"}}}
                """).RootElement));

        _mockConsole.Setup(x => x.Log(It.IsAny<string>(), It.IsAny<string>()));
        _mockConsole.Setup(x => x.LogError(It.IsAny<string>(), It.IsAny<string>()));

        var reconnectTask = _reconnector.ScheduleReconnectAsync(CancellationToken.None);
        await connectCalled.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, callCount);
        _mockConsole.Verify(x => x.LogError("gateway",
            It.Is<string>(s => s.Contains("Pairing required"))), Times.AtLeastOnce);
        _mockConsole.Verify(x => x.Log("gateway",
            It.Is<string>(s => s.Contains("openclaw devices list"))), Times.AtLeastOnce);

        _cts.Cancel();
        try { await reconnectTask.WaitAsync(TimeSpan.FromSeconds(1)); } catch { /* ignore */ }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        try { _reconnector.Dispose(); } catch { /* ignore */ }
    }
}

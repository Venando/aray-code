using ArayCode;
using ArayCode.Services;
using ArayCode.Services.Diagnostics;

public class GatewayReconnector : IDisposable
{
    private readonly SemaphoreSlim _reconnectLock = new SemaphoreSlim(1, 1);
    private readonly IGatewayConnector _gatewayConnector;
    private readonly IColorConsole _console;
    private readonly AppConfig _cfg;
    private readonly CancellationToken _cancellationToken;

    private bool _isReconnecting = false;
    private Task? _reconnectTask = null;

    public SemaphoreSlim ReconnectLock => _reconnectLock;

    /// <summary>Fires when the reconnection loop begins (initial delay before first attempt).</summary>
    public event Action? ReconnectStarted;

    /// <summary>Fires after a successful reconnection.</summary>
    public event Action? ReconnectSucceeded;

    /// <summary>Fires when the reconnection loop exhausts all retries without success.</summary>
    public event Action? ReconnectFailed;

    /// <summary>Maximum number of reconnection attempts before giving up. Default: 5.</summary>
    public int MaxRetryCount { get; set; } = 5;

    public GatewayReconnector(AppConfig appConfig, IColorConsole console, IGatewayConnector gatewayConnector, CancellationToken cancellationToken)
    {
        _cfg = appConfig;
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _cancellationToken = cancellationToken;
        _gatewayConnector = gatewayConnector;
    }

    public async Task ScheduleReconnectAsync(CancellationToken ct)
    {
        if (_cancellationToken.IsCancellationRequested) return;

        await _reconnectLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_isReconnecting) return;
            _isReconnecting = true;
        }
        finally
        {
            _reconnectLock.Release();
        }
        _console.Log("gateway", "Starting reconnection loop...");
        ReconnectStarted?.Invoke();
        _reconnectTask = ReconnectLoopAsync(ct);
    }

    private async Task ReconnectLoopAsync(CancellationToken ct)
    {
        var linkCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cancellationToken);
        var linkedCt = linkCts.Token;
        try
        {
            int attempt = 0;
            while (!linkedCt.IsCancellationRequested && attempt < MaxRetryCount)
            {
                attempt++;
                var delayMs = CalculateBackoffDelay(attempt);
                _console.Log("gateway", $"Waiting {delayMs / 1000.0:F1}s before reconnection attempt {attempt}/{MaxRetryCount}...");
                await Task.Delay(delayMs, linkedCt).ConfigureAwait(false);

                _console.Log("gateway", $"Attempting to reconnect (attempt {attempt}/{MaxRetryCount})...");
                try
                {
                    await _gatewayConnector.ConnectAsync(linkedCt).ConfigureAwait(false);
                    _console.LogOk("gateway", "Reconnected successfully.");
                    ReconnectSucceeded?.Invoke();
                    return;
                }
                catch (OperationCanceledException) when (linkedCt.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    var classification = GatewayErrorClassifier.Classify(ex);
                    if (!classification.ShouldRetry)
                    {
                        _console.LogError("gateway", classification.HumanMessage);
                        if (classification.SuggestedActions.Length > 0)
                        {
                            _console.Log("gateway", "Suggested actions:");
                            foreach (var action in classification.SuggestedActions)
                                _console.Log("gateway", $"  - {action}");
                        }
                        if (classification.ShouldStopApp)
                        {
                            _console.LogError("gateway", "Fatal error — the application cannot continue. Please restart.");
                        }
                        break;
                    }
                    _console.LogError("gateway", $"Reconnection attempt {attempt}/{MaxRetryCount} failed: {ex.Message}");
                }
            }

            // All retries exhausted or non-retryable error
            _console.LogError("gateway", $"Reconnection failed after {attempt} attempts. Giving up.");
            ReconnectFailed?.Invoke();
        }
        finally
        {
            _isReconnecting = false;
            linkCts.Dispose();
        }
    }

    /// <summary>
    /// Calculates the delay before a reconnection attempt using exponential backoff.
    /// Base delay from <see cref="AppConfig.ReconnectDelaySeconds"/>, doubled per attempt,
    /// capped at 60 seconds.
    /// </summary>
    private int CalculateBackoffDelay(int attempt)
    {
        var baseDelayMs = (int)(_cfg.ReconnectDelaySeconds * 1000);
        var backoffMs = baseDelayMs * (int)Math.Pow(2, attempt - 1);
        // Cap at 60s, never go below base delay (0 if config is 0)
        return Math.Min(backoffMs, 60_000);
    }

    public void Dispose()
    {
        _reconnectTask?.Wait(TimeSpan.FromSeconds(5));
        _reconnectLock.Dispose();
    }

}

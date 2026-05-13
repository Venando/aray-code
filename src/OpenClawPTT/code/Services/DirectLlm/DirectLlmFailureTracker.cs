namespace OpenClawPTT.Services;

/// <summary>
/// Tracks consecutive send failures for Direct LLM.
/// Fires <see cref="FailureThresholdReached"/> when failures hit the threshold,
/// and <see cref="FailureRecovered"/> when a success follows a failure state.
/// Thread-safe via lock.
/// </summary>
public sealed class DirectLlmFailureTracker : IDirectLlmFailureTracker, IDisposable
{
    private readonly int _threshold;
    private int _consecutiveFailures;
    private bool _wasHealthy = true;
    private bool _disposed;
    private readonly object _lock = new();

    public DirectLlmFailureTracker(int threshold = 1)
    {
        if (threshold < 1)
            throw new ArgumentOutOfRangeException(nameof(threshold), threshold, "Threshold must be >= 1");
        _threshold = threshold;
    }

    public event Action? FailureThresholdReached;
    public event Action? FailureRecovered;

    public int ConsecutiveFailures
    {
        get { lock (_lock) return _consecutiveFailures; }
    }

    public int Threshold => _threshold;

    public bool WasHealthy
    {
        get { lock (_lock) return _wasHealthy; }
    }

    public void RecordSuccess()
    {
        Action? handler = null;
        lock (_lock)
        {
            _consecutiveFailures = 0;

            if (!_wasHealthy)
            {
                _wasHealthy = true;
                handler = FailureRecovered;
            }
        }
        handler?.Invoke();
    }

    public void RecordFailure()
    {
        Action? handler = null;
        lock (_lock)
        {
            _consecutiveFailures++;

            if (_wasHealthy && _consecutiveFailures >= _threshold)
            {
                _wasHealthy = false;
                handler = FailureThresholdReached;
            }
        }
        handler?.Invoke();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            FailureThresholdReached = null;
            FailureRecovered = null;
        }
    }
}

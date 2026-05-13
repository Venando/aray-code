namespace OpenClawPTT.Services;

/// <summary>
/// Tracks consecutive send failures for Direct LLM and fires events
/// when the failure threshold is reached or when recovery occurs.
/// </summary>
public interface IDirectLlmFailureTracker
{
    /// <summary>Fired when consecutive failures reach the configured threshold.</summary>
    event Action? FailureThresholdReached;

    /// <summary>Fired when a send succeeds after being in a failed state.</summary>
    event Action? FailureRecovered;

    /// <summary>Record a successful send — resets the failure counter.</summary>
    void RecordSuccess();

    /// <summary>Record a failed send — increments the failure counter.</summary>
    void RecordFailure();

    /// <summary>Current consecutive failure count.</summary>
    int ConsecutiveFailures { get; }

    /// <summary>Threshold before <see cref="FailureThresholdReached"/> fires.</summary>
    int Threshold { get; }

    /// <summary>
    /// True if the tracker is currently in a healthy state (no threshold breach).
    /// Starts true, goes false after threshold reached, goes true again after recovery.
    /// </summary>
    bool WasHealthy { get; }
}

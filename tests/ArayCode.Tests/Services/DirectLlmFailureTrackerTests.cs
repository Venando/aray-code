using Xunit;
using ArayCode.Services;

namespace ArayCode.Tests;

public class DirectLlmFailureTrackerTests
{
    // ── RED tests: write them first, watch them fail ──────────────────────

    [Fact]
    public void ConsecutiveFailures_StartsAtZero()
    {
        var tracker = new DirectLlmFailureTracker();
        Assert.Equal(0, tracker.ConsecutiveFailures);
    }

    [Fact]
    public void RecordSuccess_KeepsCountAtZero()
    {
        var tracker = new DirectLlmFailureTracker();
        tracker.RecordSuccess();
        Assert.Equal(0, tracker.ConsecutiveFailures);
    }

    [Fact]
    public void RecordFailure_IncrementsCount()
    {
        var tracker = new DirectLlmFailureTracker();
        tracker.RecordFailure();
        Assert.Equal(1, tracker.ConsecutiveFailures);
    }

    [Fact]
    public void RecordSuccess_ResetsCountToZero()
    {
        var tracker = new DirectLlmFailureTracker();
        tracker.RecordFailure();
        tracker.RecordFailure();
        tracker.RecordFailure();
        Assert.Equal(3, tracker.ConsecutiveFailures);

        tracker.RecordSuccess();
        Assert.Equal(0, tracker.ConsecutiveFailures);
    }

    [Fact]
    public void FailureThresholdReached_Fires_OnFirstFailure()
    {
        var tracker = new DirectLlmFailureTracker(threshold: 1);
        bool fired = false;
        tracker.FailureThresholdReached += () => fired = true;

        tracker.RecordFailure();

        Assert.True(fired);
    }

    [Fact]
    public void FailureThresholdReached_DoesNotFire_OnSubsequentFailures()
    {
        var tracker = new DirectLlmFailureTracker(threshold: 1);
        int fireCount = 0;
        tracker.FailureThresholdReached += () => fireCount++;

        tracker.RecordFailure(); // fires
        tracker.RecordFailure(); // should NOT fire (already over threshold)
        tracker.RecordFailure(); // should NOT fire

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void FailureRecovered_Fires_WhenSuccessAfterFailure()
    {
        var tracker = new DirectLlmFailureTracker(threshold: 1);
        bool recovered = false;
        tracker.FailureRecovered += () => recovered = true;

        tracker.RecordFailure(); // fails first
        tracker.RecordSuccess(); // recovers

        Assert.True(recovered);
    }

    [Fact]
    public void FailureRecovered_DoesNotFire_WhenAlreadyHealthy()
    {
        var tracker = new DirectLlmFailureTracker(threshold: 1);
        bool recovered = false;
        tracker.FailureRecovered += () => recovered = true;

        tracker.RecordSuccess(); // already healthy

        Assert.False(recovered);
    }

    [Fact]
    public void FailureRecovered_Resets_SoNextFailureFireAgain()
    {
        var tracker = new DirectLlmFailureTracker(threshold: 1);
        int recoveredCount = 0;
        int failedCount = 0;
        tracker.FailureRecovered += () => recoveredCount++;
        tracker.FailureThresholdReached += () => failedCount++;

        // cycle 1
        tracker.RecordFailure();
        tracker.RecordSuccess();
        Assert.Equal(1, failedCount);
        Assert.Equal(1, recoveredCount);

        // cycle 2
        tracker.RecordFailure();
        tracker.RecordSuccess();
        Assert.Equal(2, failedCount);
        Assert.Equal(2, recoveredCount);
    }

    [Fact]
    public void Threshold3_FiresOnThirdFailure()
    {
        var tracker = new DirectLlmFailureTracker(threshold: 3);
        int fireCount = 0;
        tracker.FailureThresholdReached += () => fireCount++;

        tracker.RecordFailure(); // 1
        Assert.Equal(0, fireCount);

        tracker.RecordFailure(); // 2
        Assert.Equal(0, fireCount);

        tracker.RecordFailure(); // 3
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void Threshold3_ResetsOnSuccess_BeforeReaching()
    {
        var tracker = new DirectLlmFailureTracker(threshold: 3);
        int fireCount = 0;
        tracker.FailureThresholdReached += () => fireCount++;

        tracker.RecordFailure(); // 1
        tracker.RecordFailure(); // 2
        tracker.RecordSuccess(); // reset
        tracker.RecordFailure(); // 1 again
        tracker.RecordFailure(); // 2 again

        Assert.Equal(0, fireCount); // never reached 3
    }

    [Fact]
    public void WasHealthy_StartsTrue()
    {
        var tracker = new DirectLlmFailureTracker();
        Assert.True(tracker.WasHealthy);
    }

    [Fact]
    public void WasHealthy_GoesFalse_OnFirstFailure()
    {
        var tracker = new DirectLlmFailureTracker(threshold: 1);
        Assert.True(tracker.WasHealthy);

        tracker.RecordFailure();
        Assert.False(tracker.WasHealthy);
    }

    [Fact]
    public void WasHealthy_GoesTrue_AfterRecovery()
    {
        var tracker = new DirectLlmFailureTracker(threshold: 1);
        tracker.RecordFailure();
        Assert.False(tracker.WasHealthy);

        tracker.RecordSuccess();
        Assert.True(tracker.WasHealthy);
    }

    [Fact]
    public void WasHealthy_StaysTrue_WhenBelowThreshold()
    {
        var tracker = new DirectLlmFailureTracker(threshold: 3);
        tracker.RecordFailure();
        Assert.True(tracker.WasHealthy); // still below threshold

        tracker.RecordFailure();
        Assert.True(tracker.WasHealthy); // still below

        tracker.RecordFailure();
        Assert.False(tracker.WasHealthy); // now at threshold
    }
}

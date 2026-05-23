using System.Collections.Concurrent;

namespace ArayCode.Services;

/// <summary>
/// Default implementation of <see cref="IBackgroundJobRunner"/>.
/// Tracks jobs via a ConcurrentDictionary with bounded history.
/// Fire-and-forget tasks are wrapped in try/catch with logging and event notification.
/// </summary>
public sealed class BackgroundJobRunner : IBackgroundJobRunner, IDisposable
{
    private readonly ConcurrentDictionary<Guid, JobInfo> _runningJobs = new();
    private readonly ConcurrentQueue<JobInfo> _history = new();
    private readonly Action<string>? _logCallback;
    private readonly int _maxHistory;
    private readonly object _historyLock = new();
    private bool _disposed;

    public event EventHandler<JobErrorEventArgs>? JobFailed;

    /// <summary>
    /// Creates a new BackgroundJobRunner.
    /// </summary>
    /// <param name="logCallback">Optional callback for logging messages (e.g. _console.Log from callers).</param>
    /// <param name="maxHistory">Maximum number of completed jobs to retain in history. Default 100.</param>
    public BackgroundJobRunner(Action<string>? logCallback = null, int maxHistory = 100)
    {
        _logCallback = logCallback;
        _maxHistory = maxHistory;
    }

    public Task<TResult> RunAsync<TResult>(Func<TResult> action, string jobName, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BackgroundJobRunner));
        var jobId = Guid.NewGuid();
        var jobInfo = new JobInfo(jobId, jobName, DateTime.UtcNow, JobStatus.Running);

        _runningJobs[jobId] = jobInfo;
        Log($"Job started: {jobName} ({jobId})");

        try
        {
            var result = action();
            var duration = DateTime.UtcNow - jobInfo.StartedAt;
            UpdateJobStatus(jobId, JobStatus.Completed);
            Log($"Job completed: {jobName} ({jobId}) in {duration.TotalMilliseconds:F0}ms");
            return Task.FromResult(result);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            var duration = DateTime.UtcNow - jobInfo.StartedAt;
            UpdateJobStatus(jobId, JobStatus.Faulted);
            Log($"Job faulted: {jobName} ({jobId}): {ex.Message}");
            OnJobFailed(jobInfo, ex);
            throw;
        }
    }

    public async Task RunAsync(Func<Task> action, string jobName, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BackgroundJobRunner));
        var jobId = Guid.NewGuid();
        var jobInfo = new JobInfo(jobId, jobName, DateTime.UtcNow, JobStatus.Running);

        _runningJobs[jobId] = jobInfo;
        Log($"Job started: {jobName} ({jobId})");

        try
        {
            await action();
            var duration = DateTime.UtcNow - jobInfo.StartedAt;
            UpdateJobStatus(jobId, JobStatus.Completed);
            Log($"Job completed: {jobName} ({jobId}) in {duration.TotalMilliseconds:F0}ms");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            var duration = DateTime.UtcNow - jobInfo.StartedAt;
            UpdateJobStatus(jobId, JobStatus.Faulted);
            Log($"Job faulted: {jobName} ({jobId}): {ex.Message}");
            OnJobFailed(jobInfo, ex);
            throw;
        }
    }

    public void RunAndForget(Action action, string jobName)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BackgroundJobRunner));

        var jobId = Guid.NewGuid();
        var jobInfo = new JobInfo(jobId, jobName, DateTime.UtcNow, JobStatus.Running);

        _runningJobs[jobId] = jobInfo;
        Log($"Fire-and-forget started: {jobName} ({jobId})");

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                action();
                var duration = DateTime.UtcNow - jobInfo.StartedAt;
                UpdateJobStatus(jobId, JobStatus.Completed);
                Log($"Fire-and-forget completed: {jobName} ({jobId}) in {duration.TotalMilliseconds:F0}ms");
            }
            catch (Exception ex)
            {
                var duration = DateTime.UtcNow - jobInfo.StartedAt;
                UpdateJobStatus(jobId, JobStatus.Faulted);
                Log($"Fire-and-forget faulted: {jobName} ({jobId}): {ex.Message}");
                OnJobFailed(jobInfo, ex);
            }
        });
    }

    public void RunAndForget(Func<Task> action, string jobName)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(BackgroundJobRunner));

        var jobId = Guid.NewGuid();
        var jobInfo = new JobInfo(jobId, jobName, DateTime.UtcNow, JobStatus.Running);

        _runningJobs[jobId] = jobInfo;
        Log($"Fire-and-forget started: {jobName} ({jobId})");

        _ = Task.Run(async () =>
        {
            try
            {
                await action();
                var duration = DateTime.UtcNow - jobInfo.StartedAt;
                UpdateJobStatus(jobId, JobStatus.Completed);
                Log($"Fire-and-forget completed: {jobName} ({jobId}) in {duration.TotalMilliseconds:F0}ms");
            }
            catch (Exception ex)
            {
                var duration = DateTime.UtcNow - jobInfo.StartedAt;
                UpdateJobStatus(jobId, JobStatus.Faulted);
                Log($"Fire-and-forget faulted: {jobName} ({jobId}): {ex.Message}");
                OnJobFailed(jobInfo, ex);
            }
        });
    }

    public IReadOnlyList<JobInfo> GetActiveJobs()
    {
        return _runningJobs.Values.ToList().AsReadOnly();
    }

    private void UpdateJobStatus(Guid jobId, JobStatus status)
    {
        if (_runningJobs.TryGetValue(jobId, out var existing))
        {
            var updated = existing with { Status = status };
            _runningJobs.TryUpdate(jobId, updated, existing);

            // Move completed/faulted jobs to history (remove from active)
            if (status != JobStatus.Running)
            {
                _runningJobs.TryRemove(jobId, out _);
                EnqueueHistory(updated);
            }
        }
    }

    private void EnqueueHistory(JobInfo jobInfo)
    {
        lock (_historyLock)
        {
            _history.Enqueue(jobInfo);
            while (_history.Count > _maxHistory && _history.TryDequeue(out _)) { }
        }
    }

    private void OnJobFailed(JobInfo jobInfo, Exception exception)
    {
        var context = $"Background job '{jobInfo.JobName}' ({jobInfo.JobId}) failed.";
        JobFailed?.Invoke(this, new JobErrorEventArgs(jobInfo, exception, context));
    }

    private void Log(string message)
    {
        _logCallback?.Invoke(message);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _runningJobs.Clear();
        lock (_historyLock) { while (_history.TryDequeue(out _)) { } }
    }
}

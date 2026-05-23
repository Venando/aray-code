namespace ArayCode.Services;

/// <summary>
/// Provides structured background job execution with error handling,
/// tracking, and monitoring support.
/// </summary>
public interface IBackgroundJobRunner
{
    /// <summary>
    /// Runs the specified action as a tracked async operation and awaits completion.
    /// </summary>
    Task<TResult> RunAsync<TResult>(Func<TResult> action, string jobName, CancellationToken ct = default);

    /// <summary>
    /// Runs the specified async action as a tracked operation and awaits completion.
    /// </summary>
    Task RunAsync(Func<Task> action, string jobName, CancellationToken ct = default);

    /// <summary>
    /// Fires the specified action as a fire-and-forget operation with error handling.
    /// </summary>
    void RunAndForget(Action action, string jobName);

    /// <summary>
    /// Fires the specified async action as a fire-and-forget operation with error handling.
    /// </summary>
    void RunAndForget(Func<Task> action, string jobName);

    /// <summary>
    /// Gets a snapshot of currently tracked jobs.
    /// </summary>
    IReadOnlyList<JobInfo> GetActiveJobs();

    /// <summary>
    /// Raised when a fire-and-forget or tracked job fails with an exception.
    /// </summary>
    event EventHandler<JobErrorEventArgs>? JobFailed;
}

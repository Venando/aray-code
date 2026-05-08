using OpenClawPTT.Services;
using System.Threading;
using Xunit;

namespace OpenClawPTT.Tests.Services;

/// <summary>
/// Tests for <see cref="BackgroundJobRunner"/>.
/// </summary>
public class BackgroundJobRunnerTests
{
    // ─── RunAsync (sync action) ──────────────────────────────────────

    [Fact]
    public async Task RunAsync_SyncAction_CompletesSuccessfully()
    {
        var runner = new BackgroundJobRunner();
        var result = await runner.RunAsync(() => 42, "test-job");

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task RunAsync_SyncAction_TracksJobStatus()
    {
        var runner = new BackgroundJobRunner();
        var result = await runner.RunAsync(() => "hello", "greeting");

        Assert.Equal("hello", result);
        Assert.Empty(runner.GetActiveJobs()); // completed jobs are moved to history
    }

    [Fact]
    public async Task RunAsync_SyncAction_ThrowsException()
    {
        var runner = new BackgroundJobRunner();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync<int>(() => throw new InvalidOperationException("fail"), "failing-job"));

        Assert.Equal("fail", ex.Message);
        Assert.Empty(runner.GetActiveJobs());
    }

    [Fact]
    public async Task RunAsync_SyncAction_FiresJobFailedEvent()
    {
        var runner = new BackgroundJobRunner();
        JobErrorEventArgs? captured = null;
        runner.JobFailed += (_, args) => captured = args;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync<int>(() => throw new InvalidOperationException("boom"), "boom-job"));

        Assert.NotNull(captured);
        Assert.Equal("boom-job", captured!.JobInfo.JobName);
        Assert.Equal("boom", captured.Exception.Message);
    }

    // ─── RunAsync (async action) ─────────────────────────────────────

    [Fact]
    public async Task RunAsync_AsyncAction_CompletesSuccessfully()
    {
        var runner = new BackgroundJobRunner();
        var completed = false;
        await runner.RunAsync(async () =>
        {
            await Task.Yield();
            completed = true;
        }, "async-job");

        Assert.True(completed);
        Assert.Empty(runner.GetActiveJobs());
    }

    [Fact]
    public async Task RunAsync_AsyncAction_ThrowsException()
    {
        var runner = new BackgroundJobRunner();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("async fail");
            }, "async-fail"));

        Assert.Equal("async fail", ex.Message);
    }

    // ─── RunAndForget (Action) ───────────────────────────────────────

    [Fact]
    public void RunAndForget_Action_ExecutesAndCompletes()
    {
        var runner = new BackgroundJobRunner();
        using var completed = new ManualResetEventSlim(false);
        var flag = false;

        runner.RunAndForget(() => { flag = true; completed.Set(); }, "simple-action");

        Assert.True(completed.Wait(1000), "Timed out waiting for fire-and-forget action");
        Assert.True(flag);
    }

    [Fact]
    public void RunAndForget_Action_HandlesException()
    {
        var runner = new BackgroundJobRunner();
        using var errorReceived = new ManualResetEventSlim(false);
        JobErrorEventArgs? captured = null;
        runner.JobFailed += (_, args) => { captured = args; errorReceived.Set(); };

        runner.RunAndForget(() => throw new InvalidOperationException("fire-and-forget boom"), "boom");

        Assert.True(errorReceived.Wait(1000), "Timed out waiting for JobFailed event");
        Assert.NotNull(captured);
        Assert.Equal("boom", captured!.JobInfo.JobName);
        Assert.IsType<InvalidOperationException>(captured.Exception);
        Assert.Equal("fire-and-forget boom", captured.Exception.Message);
    }

    // ─── RunAndForget (async) ────────────────────────────────────────

    [Fact]
    public void RunAndForget_AsyncAction_ExecutesAndCompletes()
    {
        var runner = new BackgroundJobRunner();
        using var completed = new ManualResetEventSlim(false);
        var flag = false;

        runner.RunAndForget(async () =>
        {
            await Task.Yield();
            flag = true;
            completed.Set();
        }, "async-action");

        Assert.True(completed.Wait(1000), "Timed out waiting for fire-and-forget async action");
        Assert.True(flag);
    }

    [Fact]
    public void RunAndForget_AsyncAction_HandlesException()
    {
        var runner = new BackgroundJobRunner();
        using var errorReceived = new ManualResetEventSlim(false);
        JobErrorEventArgs? captured = null;
        runner.JobFailed += (_, args) => { captured = args; errorReceived.Set(); };

        runner.RunAndForget(async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("async fire-and-forget boom");
        }, "async-boom");

        Assert.True(errorReceived.Wait(1000), "Timed out waiting for JobFailed event");
        Assert.NotNull(captured);
        Assert.Equal("async-boom", captured!.JobInfo.JobName);
        Assert.Equal("async fire-and-forget boom", captured.Exception.Message);
    }

    // ─── GetActiveJobs ───────────────────────────────────────────────

    [Fact]
    public void GetActiveJobs_ReturnsRunningJobs()
    {
        var runner = new BackgroundJobRunner();
        var tcs = new TaskCompletionSource();

        runner.RunAndForget(() =>
        {
            tcs.Task.Wait(); // block until we let it go
        }, "blocking-job");

        var active = runner.GetActiveJobs();
        Assert.Single(active);
        Assert.Equal("blocking-job", active[0].JobName);
        Assert.Equal(JobStatus.Running, active[0].Status);

        tcs.SetResult();
    }

    [Fact]
    public void GetActiveJobs_EmptyWhenNoJobs()
    {
        var runner = new BackgroundJobRunner();
        Assert.Empty(runner.GetActiveJobs());
    }

    // ─── Log callback ────────────────────────────────────────────────

    [Fact]
    public void Constructor_AcceptsLogCallback()
    {
        var messages = new List<string>();
        var runner = new BackgroundJobRunner(msg => messages.Add(msg));

        runner.RunAndForget(() => { }, "logged-job");
        Assert.NotEmpty(messages);
        Assert.Contains(messages, m => m.Contains("Fire-and-forget started: logged-job"));
    }

    // ─── DisposedGuard ───────────────────────────────────────────────

    [Fact]
    public void RunAndForget_AfterDispose_ThrowsObjectDisposedException()
    {
        var runner = new BackgroundJobRunner();
        runner.Dispose();

        Assert.Throws<ObjectDisposedException>(() => runner.RunAndForget(() => { }, "boom"));
    }

    [Fact]
    public async Task RunAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var runner = new BackgroundJobRunner();
        runner.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await runner.RunAsync(() => 42, "boom"));
    }

    [Fact]
    public async Task Dispose_Twice_DoesNotThrow()
    {
        var runner = new BackgroundJobRunner();
        runner.Dispose();
        var exception = Record.Exception(() => runner.Dispose());
        Assert.Null(exception);
    }

    // ─── JobInfo properties ──────────────────────────────────────────

    [Fact]
    public void JobInfo_RecordsAllProperties()
    {
        var id = Guid.NewGuid();
        var started = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        var info = new JobInfo(id, "test-job", started, JobStatus.Running);

        Assert.Equal(id, info.JobId);
        Assert.Equal("test-job", info.JobName);
        Assert.Equal(started, info.StartedAt);
        Assert.Equal(JobStatus.Running, info.Status);
    }

    [Fact]
    public void JobResult_RecordsProperties()
    {
        var ex = new Exception("fail");
        var duration = TimeSpan.FromSeconds(2.5);
        var result = new JobResult(false, ex, duration);

        Assert.False(result.Success);
        Assert.Same(ex, result.Exception);
        Assert.Equal(duration, result.Duration);
    }

    [Fact]
    public void JobResult_Success_Defaults()
    {
        var duration = TimeSpan.FromMilliseconds(150);
        var result = new JobResult(true, null, duration);

        Assert.True(result.Success);
        Assert.Null(result.Exception);
    }

    // ─── Event Args ──────────────────────────────────────────────────

    [Fact]
    public void JobErrorEventArgs_RecordsContext()
    {
        var jobInfo = new JobInfo(Guid.NewGuid(), "err-job", DateTime.UtcNow, JobStatus.Running);
        var ex = new Exception("error");
        var args = new JobErrorEventArgs(jobInfo, ex, "custom context");

        Assert.Same(jobInfo, args.JobInfo);
        Assert.Same(ex, args.Exception);
        Assert.Equal("custom context", args.Context);
    }

    [Fact]
    public void JobErrorEventArgs_DefaultContextIsNull()
    {
        var jobInfo = new JobInfo(Guid.NewGuid(), "err-job", DateTime.UtcNow, JobStatus.Running);
        var ex = new Exception("error");
        var args = new JobErrorEventArgs(jobInfo, ex);

        Assert.Null(args.Context);
    }
}

namespace ArayCode.Services;

public class JobErrorEventArgs : EventArgs
{
    public JobInfo JobInfo { get; }
    public Exception Exception { get; }
    public string? Context { get; }

    public JobErrorEventArgs(JobInfo jobInfo, Exception exception, string? context = null)
    {
        JobInfo = jobInfo;
        Exception = exception;
        Context = context;
    }
}

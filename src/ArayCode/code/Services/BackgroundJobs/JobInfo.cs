namespace ArayCode.Services;

public record JobInfo(
    Guid JobId,
    string JobName,
    DateTime StartedAt,
    JobStatus Status
);

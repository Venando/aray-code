namespace ArayCode.Services;

public record JobResult(
    bool Success,
    Exception? Exception,
    TimeSpan Duration
);

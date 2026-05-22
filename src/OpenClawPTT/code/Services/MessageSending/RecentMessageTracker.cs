using System.Collections.Concurrent;

namespace OpenClawPTT.Services;

/// <summary>
/// Thread-safe tracker for recently sent user messages. Uses a sliding time
/// window (default 5 s) so the gateway echo-back can be recognised and
/// suppressed without printing the message twice.
/// </summary>
public sealed class RecentMessageTracker : IRecentMessageTracker
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _sent = new();
    private readonly TimeSpan _window;

    public RecentMessageTracker(TimeSpan? window = null)
    {
        _window = window ?? TimeSpan.FromSeconds(5);
    }

    public void TrackSent(string content)
    {
        if (string.IsNullOrEmpty(content)) return;
        _sent[content] = DateTimeOffset.UtcNow;
        Cleanup();
    }

    public bool WasRecentlySent(string content)
    {
        if (string.IsNullOrEmpty(content)) return false;

        Cleanup();

        return _sent.TryGetValue(content, out var ts)
            && DateTimeOffset.UtcNow - ts <= _window;
    }

    private void Cleanup()
    {
        var cutoff = DateTimeOffset.UtcNow - _window;
        foreach (var kvp in _sent)
        {
            if (kvp.Value < cutoff)
                _sent.TryRemove(kvp.Key, out _);
        }
    }
}

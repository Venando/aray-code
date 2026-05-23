using System.Collections.Concurrent;
using System.Text;

namespace OpenClawPTT.Services;

/// <summary>
/// Thread-safe tracker for recently sent user messages. Uses a sliding time
/// window (default 5 s) so the gateway echo-back can be recognised and
/// suppressed without printing the message twice.
///
/// Content is normalised (trimmed, whitespace-collapsed, Unicode NFC) before
/// comparison so that minor gateway-side formatting changes do not cause the
/// same message to be printed multiple times.
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
        var key = Normalise(content);
        if (string.IsNullOrEmpty(key)) return;
        _sent[key] = DateTimeOffset.UtcNow;
        Cleanup();
    }

    public bool WasRecentlySent(string content)
    {
        if (string.IsNullOrEmpty(content)) return false;

        Cleanup();

        var key = Normalise(content);
        return !string.IsNullOrEmpty(key)
            && _sent.TryGetValue(key, out var ts)
            && DateTimeOffset.UtcNow - ts <= _window;
    }

    /// <summary>
    /// Normalises message content for consistent comparison:
    /// - Trims leading/trailing whitespace
    /// - Collapses runs of internal whitespace (spaces, tabs, newlines) into a single space
    /// - Applies Unicode NFC normalization (e.g. precomposed é = U+00E9 vs decomposed e + combining accent)
    /// </summary>
    public static string Normalise(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Step 1: Unicode NFC normalisation
        var nfc = text.Normalize(NormalizationForm.FormC);

        // Step 2: Trim leading/trailing whitespace
        nfc = nfc.Trim();

        // Step 3: Collapse internal whitespace runs into single space
        var collapsed = new StringBuilder(nfc.Length);
        var inWhitespace = false;
        foreach (var ch in nfc)
        {
            if (char.IsWhiteSpace(ch))
            {
                inWhitespace = true;
            }
            else
            {
                if (inWhitespace && collapsed.Length > 0)
                    collapsed.Append(' ');
                collapsed.Append(ch);
                inWhitespace = false;
            }
        }

        return collapsed.ToString();
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

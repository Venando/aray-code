namespace OpenClawPTT.Services;

/// <summary>
/// Tracks recently sent user messages to avoid double-printing when the gateway
/// echoes them back via <c>session.message</c> events.
/// </summary>
public interface IRecentMessageTracker
{
    /// <summary>Record a message that was sent locally (and already printed).</summary>
    void TrackSent(string content);

    /// <summary>Check if a message was recently sent from this node.</summary>
    bool WasRecentlySent(string content);
}

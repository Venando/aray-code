namespace ArayCode.Services;

/// <summary>
/// Lifecycle phases of a single transcription attempt.
/// </summary>
public enum TranscriptionPhase
{
    /// <summary>Transcription has started (audio captured, sending to provider).</summary>
    Started,
    /// <summary>Transcription succeeded, text is available.</summary>
    Succeeded,
    /// <summary>Transcription failed with an error.</summary>
    Failed,
    /// <summary>Transcription timed out.</summary>
    TimedOut,
}

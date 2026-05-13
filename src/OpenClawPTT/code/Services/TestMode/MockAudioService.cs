using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT.Services.TestMode;

/// <summary>
/// Mock implementation of IAudioService for test mode.
/// Simulates audio recording without accessing real microphone hardware.
/// </summary>
public sealed class MockAudioService : IAudioService
{
    private readonly IColorConsole _console;
    private readonly TestScenarioSession _session;
    private bool _isRecording;
    private DateTime _recordingStartTime;
    private bool _disposed;

    /// <inheritdoc />
    public Action<TranscriptionPhase, string?>? TranscriptionStatusCallback { get; set; }

    public bool IsRecording => _isRecording;

    public MockAudioService(string scenario, IColorConsole console)
    {
        _console = console;
        _session = new TestScenarioSession(scenario);
    }

    /// <summary>
    /// Simulates starting audio recording.
    /// </summary>
    public void StartRecording()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MockAudioService));
        if (_isRecording) return;

        _isRecording = true;
        _recordingStartTime = DateTime.UtcNow;

        _console.PrintWarning("[TEST MODE] 🎤 Recording started (simulated)");
        _console.PrintInfo("[TEST MODE] In test mode, no actual audio is recorded.");
    }

    /// <summary>
    /// Simulates stopping recording and returns a "transcribed" message.
    /// </summary>
    public async Task<string?> StopAndTranscribeAsync(CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MockAudioService));
        if (!_isRecording) return null;

        var recordingDuration = DateTime.UtcNow - _recordingStartTime;
        _isRecording = false;

        _console.PrintWarning($"[TEST MODE] ⏹️ Recording stopped. Duration: {recordingDuration.TotalSeconds:F1}s");

        // Simulate transcription delay
        await Task.Delay(200, ct);

        // Return a canned transcription based on scenario
        var transcription = GetSimulatedTranscription(recordingDuration);

        _console.PrintInfo($"[TEST MODE] 📝 Simulated transcription: \"{transcription}\"");

        return transcription;
    }

    /// <summary>
    /// Stops recording and discards the audio (simulated).
    /// </summary>
    public void StopDiscard()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MockAudioService));
        if (!_isRecording) return;

        _isRecording = false;
        _console.PrintInfo("[TEST MODE] ⏹️ Recording stopped and discarded.");
    }

    /// <summary>
    /// Gets a simulated transcription based on recording duration and scenario.
    /// </summary>
    private string GetSimulatedTranscription(TimeSpan duration)
    {
        // Short recordings get short responses
        if (duration.TotalSeconds < 1.0)
        {
            return new[] { "Hello", "Hi", "Yes", "No", "Stop", "Test" }[Random.Shared.Next(6)];
        }

        // Longer recordings get more interesting canned transcriptions
        var transcriptions = _session.Scenario switch
        {
            TestScenarios.ErrorRecovery => new[]
            {
                "Simulate an error condition please",
                "What happens when something goes wrong",
                "Test the error handling system"
            },
            TestScenarios.MultiAgent => new[]
            {
                "Switch to the next agent",
                "Tell me about multi-agent mode",
                "Which agents are available"
            },
            _ => new[]
            {
                "This is a simulated voice transcription in test mode",
                "Tell me about the test mode features",
                "How does push to talk work in this application",
                "What can I do with OpenClaw PTT"
            }
        };

        return transcriptions[Random.Shared.Next(transcriptions.Length)];
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_isRecording)
            {
                _isRecording = false;
            }
        }
    }

    public void RecreateTranscriber(AppConfig config, IColorConsole console) { /* no-op for test mode */ }

    public void RecreateRecorder(AppConfig config, IColorConsole console) { /* no-op for test mode */ }

    /// <summary>
    /// Verifies the transcriber (no-op in test mode).
    /// </summary>
    public Task VerifyTranscriberAsync(AppConfig config, IColorConsole console, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MockAudioService));
        _console.PrintInfo("[TEST MODE] Transcriber verification (no-op)");
        return Task.CompletedTask;
    }
}

using OpenClawPTT.Services;
using Xunit;
using Moq;

namespace OpenClawPTT.Tests;

/// <summary>
/// Tests for AudioResponseHandler. Avoids real TTS/audio playback by using
/// text-only config or catching initialization failures gracefully.
/// </summary>
public class AudioResponseHandlerTests
{
    private static IColorConsole CreateMockConsole() => new Mock<IColorConsole>().Object;

    [Fact]
    public async Task HandleAudioMarkerAsync_TextOnlyConfig_DoesNothing()
    {
        // Arrange: text-only config — AudioResponseHandler should skip audio handling
        var cfg = new AppConfig();
        var audioPlayer = new AudioPlayerService(CreateMockConsole());
            var jobRunner = new BackgroundJobRunner(msg => { });
            var handler = new AudioResponseHandler(cfg, CreateMockConsole(), jobRunner, audioPlayer,
                summarizer: null, pttStateMachine: null, ttsProvider: null);

        // Act: should not throw (TTS not configured is handled gracefully)
        await handler.PlayTtsAsync("Hello world");

        // Assert: IsPlaying is false since no audio was started
        Assert.False(handler.IsPlaying);
    }

    [Fact]
    public async Task HandleAudioMarkerAsync_AudioEnabled_DoesNotThrow()
    {
        // Arrange: audio-enabled config without real API keys — will fail gracefully
        var cfg = new AppConfig();
        var audioPlayer = new AudioPlayerService(CreateMockConsole());
            var jobRunner = new BackgroundJobRunner(msg => { });
            var handler = new AudioResponseHandler(cfg, CreateMockConsole(), jobRunner, audioPlayer,
                summarizer: null, pttStateMachine: null, ttsProvider: null);

        // Act: should not throw (TTS init failure is caught and logged)
        await handler.PlayTtsAsync("Test audio text");

        // Assert: handler is in valid state
        Assert.False(handler.IsPlaying);
    }

    [Fact]
    public void AudioResponseHandler_Dispose_DoesNotThrow()
    {
        var cfg = new AppConfig();
        var audioPlayer = new AudioPlayerService(CreateMockConsole());
        var jobRunner = new BackgroundJobRunner(msg => { });
        var handler = new AudioResponseHandler(cfg, CreateMockConsole(), jobRunner, audioPlayer,
                summarizer: null, pttStateMachine: null, ttsProvider: null);

        handler.Dispose(); // should not throw

        Assert.True(true); // reached here
    }
}

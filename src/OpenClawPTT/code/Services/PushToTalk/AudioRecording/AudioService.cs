using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT;
using OpenClawPTT.Transcriber;
using OpenClawPTT.VisualFeedback;
using Spectre.Console;

namespace OpenClawPTT.Services;

public sealed class AudioService : IAudioService
{
    private readonly IColorConsole _console;
    private readonly IAudioRecorder _recorder;
    private ITranscriber _transcriber;
    private readonly IVisualFeedback _visualFeedback;
    private readonly IAgentSettingsPersistence _agentSettingsPersistence;
    
    private readonly string _hotkeyCombination;
    private readonly bool _holdToTalk;
    private readonly int _rightMarginIndent;
    // Startup-only recording parameters (baked into the recorder at construction).
    private readonly int _startupSampleRate;
    private readonly int _startupChannels;
    private readonly int _startupBitsPerSample;
    private readonly int _startupMaxRecordSeconds;
    private readonly object _transcriberLock = new();
    private int _disposedFlag; // 0 = not disposed, 1 = disposed
    
    /// <summary>
    /// Creates an AudioService with a real AudioRecorder.
    /// </summary>
    public AudioService(AppConfig config, IColorConsole console, IAgentSettingsPersistence agentSettingsPersistence)
        : this(config, console, agentSettingsPersistence, recorder: null)
    {
    }
    
    /// <summary>
    /// Creates an AudioService with an injected recorder (for testing).
    /// </summary>
    internal AudioService(AppConfig config, IColorConsole console, IAgentSettingsPersistence agentSettingsPersistence, IAudioRecorder? recorder)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _agentSettingsPersistence = agentSettingsPersistence ?? throw new ArgumentNullException(nameof(agentSettingsPersistence));
        _recorder = recorder ?? new AudioRecorder(config.SampleRate, config.Channels, config.BitsPerSample, config.MaxRecordSeconds);
        _transcriber = TranscriberFactory.Create(config, console);
        _visualFeedback = VisualFeedbackFactory.Create(config);
        LogSttProvider(config);
        _hotkeyCombination = config.HotkeyCombination;
        _holdToTalk = config.HoldToTalk;
        _rightMarginIndent = config.RightMarginIndent;
        _startupSampleRate = config.SampleRate;
        _startupChannels = config.Channels;
        _startupBitsPerSample = config.BitsPerSample;
        _startupMaxRecordSeconds = config.MaxRecordSeconds;
    }
    
    public bool IsRecording => _recorder.IsRecording;
    
    public void StartRecording()
    {
        if (_disposedFlag == 1) throw new ObjectDisposedException(nameof(AudioService));
        
        _recorder.StartRecording();
        // Use per-agent hotkey if set, else fall back to global config default
        var activeAgentId = AgentRegistry.ActiveAgentId;
        var effectiveHotkey = activeAgentId != null
            ? (_agentSettingsPersistence.GetPersistedHotkey(activeAgentId) ?? _hotkeyCombination)
            : _hotkeyCombination;
        _console.PrintRecordingIndicator(true, effectiveHotkey, _holdToTalk);
        _visualFeedback.Show();
    }
    
    public void StopDiscard()
    {
        if (_disposedFlag == 1) throw new ObjectDisposedException(nameof(AudioService));
        if (!_recorder.IsRecording) return;

        _recorder.StopRecording();
        _visualFeedback.Hide();
        _console.PrintMarkup("[grey]  ─ Recording discarded ─[/]");
    }

    public async Task<string?> StopAndTranscribeAsync(CancellationToken ct)
    {
        if (_disposedFlag == 1) throw new ObjectDisposedException(nameof(AudioService));
        if (!_recorder.IsRecording) return null;
        
        var wav = _recorder.StopRecording();
        _visualFeedback.Hide();
        _console.PrintInfo("■ Recording stopped");
        
        if (wav.Length < 1024)
        {
            _console.PrintWarning("Too short (<1KB), skipped.");
            return null;
        }

        try
        {
            // Capture transcriber under lock to prevent use-after-dispose (C3)
            ITranscriber transcriber;
            lock (_transcriberLock) { transcriber = _transcriber; }
            var transcribed = await transcriber.TranscribeAsync(wav, ct: ct);
            var shellHost = _console.GetStreamShellHost();
            var prefix = $"Transcribed ({wav.Length / 1024.0:F1} KB): ";
            _console.PrintMarkup($"[green][dim]  ✓ {Markup.Escape(prefix)}[/][/] [green]{Markup.Escape(transcribed)}[/]");
            return transcribed;
        }
        catch (Exception ex)
        {
            _console.PrintError($"Transcription failed ({wav.Length / 1024.0:F1} KB): {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Re-creates the transcriber after a config change (e.g. STT provider/model switched).
    /// Disposes the old transcriber and creates a new one from the updated config.
    ///
    /// NOTE: This does NOT recreate the <see cref="IAudioRecorder"/>. Config fields
    /// <c>SampleRate</c>, <c>Channels</c>, <c>BitsPerSample</c>, and <c>MaxRecordSeconds</c>
    /// are startup-only — they are baked into the recorder at construction time.
    /// Changing them at runtime (via /reconfigure or /appconfig) silently has no effect
    /// until the app restarts.
    /// </summary>
    public void RecreateTranscriber(AppConfig config, IColorConsole console)
    {
        // Log a warning if startup-only recording parameters changed silently.
        if (config.SampleRate != _startupSampleRate ||
            config.Channels != _startupChannels ||
            config.BitsPerSample != _startupBitsPerSample ||
            config.MaxRecordSeconds != _startupMaxRecordSeconds)
        {
            console.Log("audio",
                "SampleRate/Channels/BitsPerSample/MaxRecordSeconds changed (startup-only; restart to apply).");
        }

        ITranscriber old;
        lock (_transcriberLock)
        {
            old = _transcriber;
            _transcriber = TranscriberFactory.Create(config, console);
        }
        // Dispose OUTSIDE the lock to avoid deadlocks
        old?.Dispose();
        LogSttProvider(config, recreated: true);
    }

    private void LogSttProvider(AppConfig config, bool recreated = false)
    {
        var provider = config.SttProvider ?? "groq";
        string model = provider switch
        {
            "groq" => config.GroqModel ?? "whisper-large-v3-turbo",
            "openai" => config.OpenAiModel ?? "whisper-1",
            "whisper-cpp" => config.WhisperCppModel ?? "base",
            _ => "?"
        };
        var action = recreated ? "Switched to" : "STT";
        _console.PrintMarkup($"[grey][dim]  {action}: {provider} ({model})[/][/]");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposedFlag, 1) != 0) return;
        lock (_transcriberLock)
        {
            _recorder.Dispose();
            _transcriber.Dispose();
            _visualFeedback.Dispose();
        }
    }
}
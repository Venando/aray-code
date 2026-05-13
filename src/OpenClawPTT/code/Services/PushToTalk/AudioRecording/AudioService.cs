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
    private IAudioRecorder _recorder;
    private ITranscriber _transcriber;
    private readonly IVisualFeedback _visualFeedback;
    private readonly IAgentSettingsPersistence _agentSettingsPersistence;
    
    private readonly string _hotkeyCombination;
    private readonly bool _holdToTalk;
    private readonly int _rightMarginIndent;
    private readonly object _transcriberLock = new();
    private readonly object _recorderLock = new();
    private int _disposedFlag; // 0 = not disposed, 1 = disposed
    
    /// <summary>
    /// Creates an AudioService. Uses <paramref name="recorder"/> if provided,
    /// otherwise creates a real <see cref="AudioRecorder"/> from config.
    /// </summary>
    public AudioService(AppConfig config, IColorConsole console, IAgentSettingsPersistence agentSettingsPersistence, IAudioRecorder? recorder = null)
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
    }
    
    public bool IsRecording
    {
        get { lock (_recorderLock) { return _recorder.IsRecording; } }
    }
    
    public void StartRecording()
    {
        if (_disposedFlag == 1) throw new ObjectDisposedException(nameof(AudioService));
        
        IAudioRecorder recorder;
        lock (_recorderLock) { recorder = _recorder; }
        recorder.StartRecording();
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

        IAudioRecorder recorder;
        bool wasRecording;
        lock (_recorderLock)
        {
            recorder = _recorder;
            wasRecording = _recorder.IsRecording;
        }
        if (!wasRecording) return;

        recorder.StopRecording();
        _visualFeedback.Hide();
        _console.PrintMarkup("[grey]  ─ Recording discarded ─[/]");
    }

    public async Task<string?> StopAndTranscribeAsync(CancellationToken ct)
    {
        if (_disposedFlag == 1) throw new ObjectDisposedException(nameof(AudioService));

        IAudioRecorder recorder;
        bool wasRecording;
        lock (_recorderLock)
        {
            recorder = _recorder;
            wasRecording = _recorder.IsRecording;
        }
        if (!wasRecording) return null;
        
        var wav = recorder.StopRecording();
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
    /// </summary>
    public void RecreateTranscriber(AppConfig config, IColorConsole console)
    {
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

    /// <summary>
    /// Re-creates the audio recorder after a config change (e.g. SampleRate, Channels).
    /// If a recording is in progress, logs a warning and defers — the new config
    /// applies on the next recording cycle.
    /// </summary>
    public void RecreateRecorder(AppConfig config, IColorConsole console)
    {
        IAudioRecorder? old = null;
        lock (_recorderLock)
        {
            if (_recorder.IsRecording)
            {
                console.Log("audio", "Recording in progress — new recorder settings will apply on next keypress.");
                return;
            }

            old = _recorder;
            _recorder = new AudioRecorder(
                config.SampleRate, config.Channels,
                config.BitsPerSample, config.MaxRecordSeconds);
        }
        old?.Dispose();
        console.LogOk("audio", $"Recorder updated: {config.SampleRate}Hz, {config.Channels}ch");
    }

    private void LogSttProvider(AppConfig config, bool recreated = false)
    {
        // Display the effective model name — no fallback values here.
        // TranscriberFactory/the transcriber constructors own the defaults.
        var action = recreated ? "Switched to" : "STT";
        var model = config.SttProvider switch
        {
            AppConfig.ProviderGroq => config.GroqModel,
            AppConfig.ProviderOpenAi => config.OpenAiModel,
            AppConfig.ProviderWhisperCpp => config.WhisperCppModel,
            _ => null
        };
        var providerDisplay = config.SttProvider ?? "?";
        var modelDisplay = model ?? "default";
        _console.PrintMarkup($"[grey][dim]  {action}: {providerDisplay} ({modelDisplay})[/][/]");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposedFlag, 1) != 0) return;
        IAudioRecorder? recorderToDispose;
        lock (_recorderLock)
        {
            recorderToDispose = _recorder;
            _recorder = null!;
        }
        lock (_transcriberLock)
        {
            _transcriber.Dispose();
            _visualFeedback.Dispose();
        }
        recorderToDispose?.Dispose();
    }
}
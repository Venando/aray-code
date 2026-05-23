using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT;
using OpenClawPTT.Services.Themes;
using OpenClawPTT.Transcriber;
using OpenClawPTT.VisualFeedback;
using Spectre.Console;

namespace OpenClawPTT.Services;

public sealed class AudioService : IAudioService
{
    private readonly IColorConsole _console;
    private readonly IStreamShellHost? _shellHost;
    private IAudioRecorder _recorder;
    private ITranscriber _transcriber;
    /// <inheritdoc />
    public Action<TranscriptionPhase, string?>? TranscriptionStatusCallback { get; set; }
    private IVisualFeedback _visualFeedback;
    private readonly IAgentSettingsPersistence _agentSettingsPersistence;
    
    private readonly string _hotkeyCombination;
    private readonly bool _holdToTalk;
    private readonly bool _appendToInput;
    private readonly int _rightMarginIndent;
    private readonly int _transcriptionTimeoutSeconds;
    private readonly object _transcriberLock = new();
    private readonly object _recorderLock = new();
    private int _disposedFlag; // 0 = not disposed, 1 = disposed

    private RecordingBottomPanel? _recordingPanel;
    private readonly object _panelLock = new();
    
    /// <summary>
    /// Creates an AudioService. Uses <paramref name="recorder"/> if provided,
    /// otherwise creates a real <see cref="AudioRecorder"/> from config.
    /// </summary>
    public AudioService(AppConfig config, IColorConsole console, IAgentSettingsPersistence agentSettingsPersistence, IAudioRecorder? recorder = null, IStreamShellHost? shellHost = null)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _agentSettingsPersistence = agentSettingsPersistence ?? throw new ArgumentNullException(nameof(agentSettingsPersistence));
        _shellHost = shellHost;
        _recorder = recorder ?? new AudioRecorder(config.SampleRate, config.Channels, config.BitsPerSample, config.MaxRecordSeconds);
        _transcriber = TranscriberFactory.Create(config, console);
        _visualFeedback = VisualFeedbackFactory.Create(config);
        LogSttProvider(config);
        _hotkeyCombination = config.HotkeyCombination;
        _holdToTalk = config.HoldToTalk;
        _appendToInput = config.AppendTranscriptionToInput;
        _rightMarginIndent = config.RightMarginIndent;
        _transcriptionTimeoutSeconds = config.TranscriptionTimeoutSeconds;
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
        try
        {
            recorder.StartRecording();
        }
        catch (InvalidOperationException ex)
        {
            _console.PrintError($"Cannot start recording: {ex.Message}");
            _console.PrintInfo("  Install 'sox' (Linux/macOS) or ensure NAudio is available (Windows).");
            return;
        }
        // Use per-agent hotkey if set, else fall back to global config default
        var activeAgentId = AgentRegistry.ActiveAgentId;
        var effectiveHotkey = activeAgentId != null
            ? (_agentSettingsPersistence.GetPersistedHotkey(activeAgentId) ?? _hotkeyCombination)
            : _hotkeyCombination;

        // Show recording indicator in bottom panel (fancy animated, voice-reactive)
        // Capture recorder ref under lock to avoid use-after-recreate
        IAudioRecorder captureRecorder;
        lock (_recorderLock) { captureRecorder = _recorder; }
        lock (_panelLock)
        {
            _recordingPanel?.Dispose();
            _recordingPanel = new RecordingBottomPanel(
                effectiveHotkey,
                _holdToTalk,
                appendToInput: _appendToInput,
                getAudioLevel: () => captureRecorder.GetCurrentAudioLevel());
            _shellHost?.SetBottomPanel(_recordingPanel);
        }

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
        ShowDiscarded();
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
        ShowTranscribing();
        
        if (wav.Length < 1024)
        {
            _console.PrintWarning("Recording too short — hold the hotkey for at least 0.5 seconds.");
            DismissRecordingPanel();
            return null;
        }

        TranscriptionStatusCallback?.Invoke(TranscriptionPhase.Started, null);

        try
        {
            // Capture transcriber under lock to prevent use-after-dispose (C3)
            ITranscriber transcriber;
            lock (_transcriberLock) { transcriber = _transcriber; }

            // Wrap the caller's ct with a transcription timeout
            using var timeoutCts = new CancellationTokenSource(
                TimeSpan.FromSeconds(_transcriptionTimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            var transcribed = await transcriber.TranscribeAsync(wav, ct: linkedCts.Token);
            var sizeKb = wav.Length / 1024.0;
            ShowConfirming(transcribed, sizeKb);
            TranscriptionStatusCallback?.Invoke(TranscriptionPhase.Succeeded, transcribed);
            return transcribed;
        }
        catch (OperationCanceledException)
        {
            _console.PrintWarning($"  Transcription timed out ({_transcriptionTimeoutSeconds}s)");
            TranscriptionStatusCallback?.Invoke(TranscriptionPhase.TimedOut, "Transcription timed out");
            DismissRecordingPanel();
            return null;
        }
        catch (Exception ex)
        {
            _console.PrintError($"Transcription failed ({wav.Length / 1024.0:F1} KB): {ex.Message}");
            TranscriptionStatusCallback?.Invoke(TranscriptionPhase.Failed, ex.Message);
            DismissRecordingPanel();
            return null;
        }
    }

    public void ShowTranscribing()
    {
        lock (_panelLock)
        {
            _recordingPanel?.SetTranscribing();
        }
    }

    public void ShowConfirming(string transcribed, double sizeKb)
    {
        lock (_panelLock)
        {
            _recordingPanel?.SetConfirming(transcribed, sizeKb);
        }
    }

    public void ShowDiscarded()
    {
        lock (_panelLock)
        {
            if (_recordingPanel == null) return;
            _recordingPanel.SetDiscarded();
            // Auto-dismiss after 1.5 seconds
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1.5));
                DismissRecordingPanel();
            });
        }
    }

    public void DismissRecordingPanel()
    {
        lock (_panelLock)
        {
            _recordingPanel?.Dispose();
            _recordingPanel = null;
            _shellHost?.ResetBottomPanel();
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

    /// <summary>
    /// Re-creates the visual feedback indicator after a config change.
    /// Disposes the old indicator and creates a new one from the updated config.
    /// If a recording is in progress, defers recreation — the new visual feedback
    /// applies on the next recording cycle.
    /// </summary>
    public void RecreateVisualFeedback(AppConfig config)
    {
        bool recording;
        lock (_recorderLock) { recording = _recorder.IsRecording; }

        if (recording)
        {
            // Can't safely recreate the native window during an active recording;
            // the new config applies on the next keypress.
            return;
        }

        IVisualFeedback old;
        lock (_transcriberLock)
        {
            old = _visualFeedback;
            _visualFeedback = VisualFeedbackFactory.Create(config);
        }
        old?.Dispose();
        _console.LogOk("visual", "Visual feedback updated with new configuration.");
    }

    /// <summary>
    /// Verifies the transcriber is functional by sending a minimal silence WAV
    /// through the pipeline. Runs synchronously but returns a Task for interface
    /// compatibility. Throws if the transcriber fails.
    /// </summary>
    public async Task VerifyTranscriberAsync(AppConfig config, IColorConsole console, CancellationToken ct = default)
    {
        // Create a minimal valid WAV with 0.25s of silence at 16kHz mono 16-bit —
        // fast enough to not block startup, complex enough to exercise the full pipeline.
        var silenceBytes = CreateSilenceWav(16000, 1, 16, 0.25f);

        ITranscriber transcriber;
        lock (_transcriberLock) { transcriber = _transcriber; }

        // Transcribe silence — expected to return quickly (empty result for silence).
        // If the provider is unreachable or the binary/model is broken, this throws.
        await transcriber.TranscribeAsync(silenceBytes, "verify.wav", ct);
        console.LogOk("stt", $"Transcriber verified ({config.SttProvider ?? "?"})");
    }

    /// <summary>
    /// Creates a minimal valid PCM WAV file containing silence.
    /// </summary>
    private static byte[] CreateSilenceWav(int sampleRate, int channels, int bitsPerSample, float durationSec)
    {
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        int blockAlign = channels * bitsPerSample / 8;
        int dataSize = (int)(byteRate * durationSec);
        // Align to block boundary
        dataSize = dataSize / blockAlign * blockAlign;

        using var ms = new System.IO.MemoryStream(44 + dataSize);
        using var bw = new System.IO.BinaryWriter(ms);

        // RIFF header
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataSize);  // File size - 8
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

        // fmt chunk
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);             // Chunk size (PCM)
        bw.Write((short)1);       // Audio format (PCM)
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)blockAlign);
        bw.Write((short)bitsPerSample);

        // data chunk
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(dataSize);
        bw.Write(new byte[dataSize]); // Silence

        return ms.ToArray();
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
            AppConfig.ProviderFasterWhisper => config.FasterWhisperModel,
            _ => null
        };
        var providerDisplay = config.SttProvider ?? "?";
        var modelDisplay = model ?? "default";
        var tools = ThemeProvider.Current.Tools;
        _console.PrintMarkup($"[{tools.General.Muted}][{tools.General.TruncatedMore}]  {action}: {providerDisplay} ({modelDisplay})[/][/]");
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
        DismissRecordingPanel();
    }
}
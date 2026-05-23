using NAudio.Wave;

namespace ArayCode;

/// <summary>
/// Records microphone audio into WAV (16 kHz mono 16-bit by default).
/// Uses NAudio on Windows.  Falls back to `sox rec` on macOS/Linux.
/// Provides audio level monitoring for voice-reactive visualizations.
/// </summary>
public sealed class AudioRecorder : IAudioRecorder
{
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly int _bits;
    private readonly int _maxSeconds;

    // NAudio path
    private WaveInEvent? _waveIn;
    private MemoryStream? _memStream;
    private WaveFileWriter? _writer;

    // CLI fallback path
    private System.Diagnostics.Process? _recProc;
    private string? _tmpFile;

    private bool _recording;

    // Audio level monitoring
    private readonly object _levelLock = new();
    private float _currentLevel; // 0.0–1.0 normalized RMS (NAudio), or -1 for CLI
    private int _levelUnavailableFlag; // 0 = available, 1 = unavailable

    public bool IsRecording => _recording;

    public float GetCurrentAudioLevel()
    {
        if (_levelUnavailableFlag == 1)
            return -1f;
        lock (_levelLock)
            return _currentLevel;
    }

    public AudioRecorder(int sampleRate = 16_000, int channels = 1, int bits = 16, int maxSeconds = 120)
    {
        _sampleRate = sampleRate;
        _channels = channels;
        _bits = bits;
        _maxSeconds = maxSeconds;
    }

    // ─── start ──────────────────────────────────────────────────────

    public void StartRecording()
    {
        if (_recording) return;

        // Reset audio level state
        lock (_levelLock)
            _currentLevel = 0f;
        Interlocked.Exchange(ref _levelUnavailableFlag, 0);

        if (OperatingSystem.IsWindows())
            StartNAudio();
        else
            StartCli();

        _recording = true;
    }

    private void StartNAudio()
    {
        var fmt = new WaveFormat(_sampleRate, _bits, _channels);
        _memStream = new MemoryStream();
        _writer = new WaveFileWriter(_memStream, fmt);

        _waveIn = new WaveInEvent
        {
            WaveFormat = fmt,
            BufferMilliseconds = 50
        };
        _waveIn.DataAvailable += (_, e) =>
        {
            if (_writer == null) return;
            _writer.Write(e.Buffer, 0, e.BytesRecorded);

            // Compute RMS audio level from the buffer (16-bit signed PCM)
            if (_bits == 16 && e.BytesRecorded >= 2)
            {
                float sumSquares = 0f;
                int sampleCount = e.BytesRecorded / 2;
                for (int i = 0; i < sampleCount; i++)
                {
                    short sample = (short)(e.Buffer[i * 2] | (e.Buffer[i * 2 + 1] << 8));
                    sumSquares += sample * sample;
                }
                float rms = MathF.Sqrt(sumSquares / sampleCount);
                // Normalize: 16-bit max amplitude = 32768, RMS for full-range sine ~23170
                // Normalize to 0–1 with a reasonable ceiling
                float level = Math.Min(rms / 10000f, 1f);
                lock (_levelLock)
                    _currentLevel = level;
            }

            // enforce max duration
            if (_writer.TotalTime.TotalSeconds >= _maxSeconds)
                _waveIn?.StopRecording();
        };

        _waveIn.RecordingStopped += (_, _) => { /* handled in Stop */ };
        _waveIn.StartRecording();
    }

    private void StartCli()
    {
        // Audio level monitoring not available for CLI backends
        Interlocked.Exchange(ref _levelUnavailableFlag, 1);

        _tmpFile = Path.Combine(Path.GetTempPath(), $"oc_ptt_{Guid.NewGuid():N}.wav");

        // sox rec: works on macOS (brew install sox) and Linux (apt install sox)
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "sox",
            ArgumentList =
            {
                "-d",                               // default audio device
                "-r", _sampleRate.ToString(),
                "-c", _channels.ToString(),
                "-b", _bits.ToString(),
                "-e", "signed-integer",
                "-t", "wav",
                _tmpFile,
                "trim", "0", _maxSeconds.ToString()
            },
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            _recProc = System.Diagnostics.Process.Start(psi);
        }
        catch (Exception)
        {
            // sox not found — try arecord (Linux/ALSA)
            psi.FileName = "arecord";
            psi.ArgumentList.Clear();
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add($"S{_bits}_LE");
            psi.ArgumentList.Add("-r");
            psi.ArgumentList.Add(_sampleRate.ToString());
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(_channels.ToString());
            psi.ArgumentList.Add("-t");
            psi.ArgumentList.Add("wav");
            psi.ArgumentList.Add("-d");
            psi.ArgumentList.Add(_maxSeconds.ToString());
            psi.ArgumentList.Add(_tmpFile);
            _recProc = System.Diagnostics.Process.Start(psi)
                       ?? throw new InvalidOperationException(
                           "No audio recorder found. Install sox or NAudio (Windows).");
        }
    }

    // ─── stop ───────────────────────────────────────────────────────

    public byte[] StopRecording()
    {
        if (!_recording) return Array.Empty<byte>();
        _recording = false;

        return OperatingSystem.IsWindows()
            ? StopNAudio()
            : StopCli();
    }

    private byte[] StopNAudio()
    {
        _waveIn?.StopRecording();
        _waveIn?.Dispose();
        _waveIn = null;

        // WaveFileWriter.Dispose finalises the RIFF length headers,
        // then MemoryStream.ToArray() still returns the full buffer.
        _writer?.Dispose();
        _writer = null;

        var data = _memStream?.ToArray() ?? Array.Empty<byte>();
        _memStream?.Dispose();
        _memStream = null;

        return data;
    }

    private byte[] StopCli()
    {
        if (_recProc is { HasExited: false })
        {
            // send SIGINT / kill gracefully so sox finalises the WAV header
            try
            {
                _recProc.Kill(entireProcessTree: false);
                _recProc.WaitForExit(3_000);
            }
            catch { /* best effort */ }
        }
        _recProc?.Dispose();
        _recProc = null;

        var data = Array.Empty<byte>();
        if (_tmpFile != null && File.Exists(_tmpFile))
        {
            data = File.ReadAllBytes(_tmpFile);
            try { File.Delete(_tmpFile); } catch { }
        }
        _tmpFile = null;
        return data;
    }

    // ─── dispose ────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_recording) StopRecording();
        _waveIn?.Dispose();
        _writer?.Dispose();
        _memStream?.Dispose();
        _recProc?.Dispose();
    }
}
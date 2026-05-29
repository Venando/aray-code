using NAudio.Wave;

namespace ArayCode;

/// <summary>
/// Records microphone audio into WAV (16 kHz mono 16-bit by default).
/// Uses NAudio on Windows.  Falls back to `sox rec` on macOS/Linux.
/// Provides audio level monitoring for voice-reactive visualizations
/// on all platforms via PCM loopback on the piped stdout stream.
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
    private Task? _readerTask;

    private bool _recording;

    // Audio level monitoring
    private readonly object _levelLock = new();
    private float _currentLevel; // 0.0–1.0 normalized RMS
    private int _levelUnavailableFlag; // 0 = available, 1 = unavailable

    public bool IsRecording => _recording;

    public float GetCurrentAudioLevel()
    {
        if (_levelUnavailableFlag == 1)
        {
            return 0f;
        }
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
        _tmpFile = Path.Combine(Path.GetTempPath(), $"oc_ptt_{Guid.NewGuid():N}.wav");

        // Audio level monitoring via stdout PCM pipe
        Interlocked.Exchange(ref _levelUnavailableFlag, 0);
        lock (_levelLock)
            _currentLevel = 0f;

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
                "-",                                // stdout → enables PCM pipe for level monitoring
                "trim", "0", _maxSeconds.ToString()
            },
            RedirectStandardOutput = true,
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
            psi.ArgumentList.Add("-");  // stdout
            _recProc = System.Diagnostics.Process.Start(psi)
                       ?? throw new InvalidOperationException(
                           "No audio recorder found. Install sox or NAudio (Windows).");
        }

        _readerTask = Task.Run(() => ReadAudioStream(_recProc!.StandardOutput.BaseStream));
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
            // Kill the subprocess so its stdout pipe closes,
            // then wait for the reader task to finish and write the file.
            try
            {
                _recProc.Kill(entireProcessTree: false);
                _recProc.WaitForExit(3_000);
            }
            catch { /* best effort */ }
        }
        _recProc?.Dispose();
        _recProc = null;

        // Wait for reader to finish writing before reading the file
        if (_readerTask != null)
        {
            try { _readerTask.GetAwaiter().GetResult(); }
            catch { /* errors already handled inside ReadAudioStream */ }
            _readerTask = null;
        }

        var data = Array.Empty<byte>();
        if (_tmpFile != null && File.Exists(_tmpFile))
        {
            data = File.ReadAllBytes(_tmpFile);
            try { File.Delete(_tmpFile); } catch { }
        }
        _tmpFile = null;
        return data;
    }

    // ─── stdout reader for CLI audio level monitoring ───────────────

    /// <summary>
    /// Reads WAV audio from the CLI subprocess's stdout, computes RMS levels
    /// from the PCM data, and fixes the WAV header size fields (sox/arecord
    /// can't finalize them when streaming to stdout).
    /// </summary>
    private void ReadAudioStream(Stream source)
    {
        try
        {
            var collected = new MemoryStream();
            var buffer = new byte[4096];
            int dataChunkOffset = -1;
            int lastSearchPos = 0;

            while (true)
            {
                int bytesRead = source.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;

                collected.Write(buffer, 0, bytesRead);

                // Scan for "data" chunk marker to find where PCM samples begin
                if (dataChunkOffset < 0)
                {
                    byte[] raw = collected.GetBuffer();
                    for (int i = lastSearchPos; i <= (int)collected.Length - 8; i++)
                    {
                        if (raw[i] == 'd' && raw[i + 1] == 'a'
                                         && raw[i + 2] == 't' && raw[i + 3] == 'a')
                        {
                            // dataChunkOffset = position after "data" + 4-byte chunk size field
                            dataChunkOffset = i + 8;
                            break;
                        }
                    }
                    if (dataChunkOffset < 0)
                        lastSearchPos = Math.Max(0, (int)collected.Length - 4);
                }
                else
                {
                    // Everything past dataChunkOffset is raw PCM samples
                    ComputeLevelFromBuffer(buffer, bytesRead);
                }
            }

            // Fix WAV header size fields (sox/arecord can't finalize them
            // when writing to stdout — they don't know the total size in advance).
            byte[] allData = collected.ToArray();
            if (dataChunkOffset >= 0)
            {
                WriteUInt32LE(allData, 4, (uint)allData.Length - 8);                        // RIFF chunk size
                WriteUInt32LE(allData, dataChunkOffset - 4, (uint)allData.Length - (uint)dataChunkOffset); // data chunk size
            }

            File.WriteAllBytes(_tmpFile!, allData);
        }
        catch (Exception)
        {
            // Level monitoring unavailable — return 0 (flat waveform)
            Interlocked.Exchange(ref _levelUnavailableFlag, 1);
        }
    }

    /// <summary>
    /// Computes RMS audio level from a 16-bit signed PCM buffer and stores it.
    /// Same formula as the NAudio path.
    /// </summary>
    private void ComputeLevelFromBuffer(byte[] buffer, int count)
    {
        if (_bits != 16 || count < 2) return;

        float sumSquares = 0f;
        int sampleCount = count / 2;
        for (int i = 0; i < sampleCount; i++)
        {
            short sample = (short)(buffer[i * 2] | (buffer[i * 2 + 1] << 8));
            sumSquares += sample * sample;
        }
        float rms = MathF.Sqrt(sumSquares / sampleCount);
        float level = Math.Min(rms / 10000f, 1f);
        lock (_levelLock)
            _currentLevel = level;
    }

    private static void WriteUInt32LE(byte[] buffer, int offset, uint value)
    {
        buffer[offset]     = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
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
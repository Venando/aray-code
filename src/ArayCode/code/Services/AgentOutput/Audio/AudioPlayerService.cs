using System;
using System.Diagnostics;
using System.IO;
using NAudio.Wave;

namespace ArayCode.Services;

/// <summary>
/// Service for playing audio bytes using NAudio on Windows,
/// or CLI audio players (aplay, paplay, ffplay, sox) on Linux/macOS.
/// </summary>
public sealed class AudioPlayerService : IAudioPlayer, IDisposable
{
    private WaveOutEvent? _waveOut;
    private WaveStream? _activeStream;
    private readonly IColorConsole _console;
    private bool _disposed;

    // Linux/macOS CLI player state
    private string? _cliPlayer;       // e.g. "aplay"
    private bool _cliPlayerChecked;   // true after first probe
    private Process? _activeProcess;  // currently playing CLI process

    public AudioPlayerService(IColorConsole console)
    {
        _console = console;
    }

    /// <summary>
    /// Play audio from byte array (WAV format or raw PCM).
    /// </summary>
    public void Play(byte[] audioBytes)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AudioPlayerService));

        try
        {
            // Stop current playback; never let Stop failure block new playback
            try { Stop(); }
            catch (Exception ex) { _console.PrintWarning($"Audio stop before play failed: {ex.Message}"); }

            if (OperatingSystem.IsWindows())
            {
                PlayWindows(audioBytes);
            }
            else
            {
                PlayLinux(audioBytes);
            }
        }
        catch (Exception ex)
        {
            _console.PrintError($"Audio playback failed: {ex.Message}");
        }
    }

    private void PlayWindows(byte[] audioBytes)
    {
        MemoryStream ms = new MemoryStream(audioBytes);
        try
        {
            var reader = new WaveFileReader(ms);
            PlayInternalWindows(reader);
        }
        catch
        {
            ms.Position = 0;
            var rawStream = new RawSourceWaveStream(ms, new WaveFormat(16000, 16, 1));
            PlayInternalWindows(rawStream);
        }
    }

    private void PlayLinux(byte[] audioBytes)
    {
        var player = GetCliPlayer();
        if (player == null)
        {
            _console.PrintWarning(
                "No audio player found for Linux. " +
                "Install one of: aplay (alsa-utils), paplay (pulseaudio-utils), " +
                "ffplay (ffmpeg), or sox.");
            return;
        }

        // Write to temp file — CLI players need a file path
        var tmpFile = Path.Combine(Path.GetTempPath(), $"aray_audio_{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(tmpFile, audioBytes);

        try
        {
            PlayCli(player, tmpFile, deleteAfter: true);
        }
        catch
        {
            try { File.Delete(tmpFile); } catch { /* ignore */ }
            throw;
        }
    }

    /// <summary>
    /// Play audio from a file path.
    /// </summary>
    public void Play(string filePath)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AudioPlayerService));

        try
        {
            // Stop current playback; never let Stop failure block new playback
            try { Stop(); }
            catch (Exception ex) { _console.PrintWarning($"Audio stop before play failed: {ex.Message}"); }

            if (!File.Exists(filePath))
            {
                _console.PrintError($"Audio file not found: {filePath}");
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                var reader = new AudioFileReader(filePath);
                PlayInternalWindows(reader);
            }
            else
            {
                var player = GetCliPlayer();
                if (player == null)
                {
                    _console.PrintWarning(
                        "No audio player found for Linux. " +
                        "Install one of: aplay (alsa-utils), paplay (pulseaudio-utils), " +
                        "ffplay (ffmpeg), or sox.");
                    return;
                }
                PlayCli(player, filePath, deleteAfter: false);
            }
        }
        catch (Exception ex)
        {
            _console.PrintError($"Audio playback failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Detects available CLI audio player on Linux/macOS, caching the result.
    /// </summary>
    private string? GetCliPlayer()
    {
        if (_cliPlayerChecked)
            return _cliPlayer;

        _cliPlayerChecked = true;

        string[] candidates = { "aplay", "paplay", "ffplay", "sox" };
        foreach (var candidate in candidates)
        {
            if (IsCommandAvailable(candidate))
            {
                _cliPlayer = candidate;
                return _cliPlayer;
            }
        }

        _cliPlayer = null;
        return null;
    }

    private static bool IsCommandAvailable(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                ArgumentList = { "-c", $"command -v {command} || which {command}" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            proc.WaitForExit(2000);
            return proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(proc.StandardOutput.ReadToEnd());
        }
        catch
        {
            return false;
        }
    }

    private void PlayCli(string player, string filePath, bool deleteAfter)
    {
        var psi = new ProcessStartInfo
        {
            FileName = player,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        switch (player)
        {
            case "aplay":
                psi.ArgumentList.Add("-q");       // quiet
                psi.ArgumentList.Add(filePath);
                break;
            case "paplay":
                psi.ArgumentList.Add(filePath);
                break;
            case "ffplay":
                psi.ArgumentList.Add("-nodisp");  // no video window
                psi.ArgumentList.Add("-autoexit"); // exit when done
                psi.ArgumentList.Add("-loglevel");
                psi.ArgumentList.Add("quiet");
                psi.ArgumentList.Add(filePath);
                break;
            case "sox":
                psi.ArgumentList.Add(filePath);
                psi.ArgumentList.Add("-d");       // play to default device
                psi.ArgumentList.Add("-q");       // quiet
                break;
            default:
                psi.ArgumentList.Add(filePath);
                break;
        }

        var proc = Process.Start(psi);
        if (proc == null)
        {
            throw new InvalidOperationException($"Failed to start {player}.");
        }

        _activeProcess = proc;

        // If we wrote a temp file, clean it up when playback ends
        if (deleteAfter)
        {
            proc.EnableRaisingEvents = true;
            proc.Exited += (_, _) =>
            {
                try { File.Delete(filePath); } catch { /* ignore */ }
                try { proc.Dispose(); } catch { /* ignore */ }
            };
        }
    }

    private void PlayInternalWindows(WaveStream waveStream)
    {
        _activeStream = waveStream;
        _waveOut = new WaveOutEvent();
        _waveOut.Init(waveStream);
        _waveOut.PlaybackStopped += OnPlaybackStopped;
        _waveOut.Play();
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            _console.PrintError($"Playback error: {e.Exception.Message}");
        }
        CleanupPlayback();
    }

    /// <summary>
    /// Stop currently playing audio and release resources.
    /// </summary>
    public void Stop()
    {
        CleanupPlayback();
    }

    private void CleanupPlayback()
    {
        if (_waveOut != null)
        {
            var w = _waveOut;
            _waveOut = null;
            w.PlaybackStopped -= OnPlaybackStopped;
            try
            {
                w.Stop();
                w.Dispose();
            }
            catch (Exception ex) { _console.PrintError($"Audio cleanup failed: {ex.Message}"); }
        }

        if (_activeStream != null)
        {
            var s = _activeStream;
            _activeStream = null;
            try { s.Dispose(); } catch { /* ignore */ }
        }

        if (_activeProcess != null)
        {
            var p = _activeProcess;
            _activeProcess = null;
            try
            {
                if (!p.HasExited)
                {
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(1000);
                }
            }
            catch { /* ignore */ }
            try { p.Dispose(); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Check if audio is currently playing.
    /// </summary>
    public bool IsPlaying
    {
        get
        {
            if (_waveOut != null)
                return _waveOut.PlaybackState == PlaybackState.Playing;
            if (_activeProcess != null)
                return !_activeProcess.HasExited;
            return false;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _disposed = true;
        }
    }
}


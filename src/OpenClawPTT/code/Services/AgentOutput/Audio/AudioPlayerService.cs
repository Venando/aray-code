using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace OpenClawPTT.Services;

/// <summary>
/// Service for playing audio bytes using NAudio.
/// </summary>
public sealed class AudioPlayerService : IAudioPlayer, IDisposable
{
    private WaveOutEvent? _waveOut;
    private readonly IColorConsole _console;
    private bool _disposed;
    
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
            Stop(); // Stop any currently playing audio
            
            // Try to load as WAV, otherwise treat as raw PCM
            MemoryStream ms = new MemoryStream(audioBytes);
            
            try
            {
                // Try to create a WaveFileReader
                var reader = new WaveFileReader(ms);
                PlayInternal(reader);
            }
            catch
            {
                // Reset and try as raw PCM (16kHz, 16-bit, mono)
                ms.Position = 0;
                var rawStream = new RawSourceWaveStream(ms, new WaveFormat(16000, 16, 1));
                PlayInternal(rawStream);
            }
        }
        catch (Exception ex)
        {
            _console.PrintError($"Audio playback failed: {ex.Message}");
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
            Stop();
            
            if (!File.Exists(filePath))
            {
                _console.PrintError($"Audio file not found: {filePath}");
                return;
            }
            
            var reader = new AudioFileReader(filePath);
            PlayInternal(reader);
        }
        catch (Exception ex)
        {
            _console.PrintError($"Audio playback failed: {ex.Message}");
        }
    }
    
    private void PlayInternal(WaveStream waveStream)
    {
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
    }
    
    /// <summary>
    /// Stop currently playing audio.
    /// </summary>
    public void Stop()
    {
        if (_waveOut != null)
        {
            try
            {
                _waveOut.Stop();
                _waveOut.Dispose();
            }
            catch { /* ignore */ }
            _waveOut = null;
        }
    }
    
    /// <summary>
    /// Check if audio is currently playing.
    /// </summary>
    public bool IsPlaying => _waveOut?.PlaybackState == PlaybackState.Playing;
    
    public void Dispose()
    {
        if (!_disposed)
        {
            Stop();
            _disposed = true;
        }
    }
}

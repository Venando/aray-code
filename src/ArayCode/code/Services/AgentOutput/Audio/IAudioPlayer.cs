namespace ArayCode.Services;

public interface IAudioPlayer : IDisposable
{
    void Play(byte[] audioBytes);
    void Play(string filePath);
    void Stop();
    bool IsPlaying { get; }
}

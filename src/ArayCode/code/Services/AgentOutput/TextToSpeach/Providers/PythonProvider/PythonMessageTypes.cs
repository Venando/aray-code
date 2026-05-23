
namespace ArayCode.TTS.Providers;

[Obsolete("Use inline JSON parsing in CoquiUvTtsProvider instead.")]
public static class MessageType
{
    public const string Performance = "perf";
    public const string Warn = "warn";
    public const string Info = "info";
    public const string Debug = "debug";
    public const string Error = "error";
    public const string Ready = "ready";
    public const string Ok = "ok";
}
namespace ArayCode.Transcriber;

/// <summary>
/// Base exception for transcriber errors.
/// </summary>
public class TranscriberException : Exception
{
    public TranscriberException(string message) : base(message) { }
    public TranscriberException(string message, Exception innerException) : base(message, innerException) { }
}

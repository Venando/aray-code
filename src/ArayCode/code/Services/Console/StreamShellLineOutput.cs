using Spectre.Console;

namespace ArayCode.Services;

/// <summary>
/// An <see cref="IFormattedOutput"/> implementation that sends each completed line
/// to StreamShell immediately via <see cref="WriteLine"/>. This enables progressive
/// display of formatted text — each time the word-wrap engine starts a new line,
/// the previous line appears in the StreamShell message feed.
/// </summary>
public sealed class StreamShellLineOutput : IFormattedOutput
{
    private readonly IStreamShellHost _shellHost;
    private readonly string _prefix;
    private readonly System.Text.StringBuilder _currentLine = new();
    private bool _prefixSent;

    public StreamShellLineOutput(IStreamShellHost shellHost, string prefix)
    {
        _shellHost = shellHost;
        _prefix = prefix;
    }

    public void Write(string text) => _currentLine.Append(text);

    public void WriteLine()
    {
        if (_currentLine.Length == 0)
            return;

        var line = _currentLine.ToString().TrimEnd();
        _currentLine.Clear();

        if (string.IsNullOrEmpty(line))
        {
            _shellHost.AddMessage("");
            return;
        }

        if (!_prefixSent)
        {
            _shellHost.AddMessage(_prefix + line);
            _prefixSent = true;
        }
        else
        {
            _shellHost.AddMessage(line);
        }
    }

    /// <summary>Flushes any remaining partial line and adds a trailing empty message.</summary>
    public void Finish()
    {
        if (_currentLine.Length > 0)
        {
            var line = _currentLine.ToString().TrimEnd();
            _currentLine.Clear();

            if (!_prefixSent)
                _shellHost.AddMessage(_prefix + line);
            else
                _shellHost.AddMessage(line);
        }

        _shellHost.AddMessage("");
        _prefixSent = true;
    }

    public int WindowWidth => ConsoleMetrics.GetWindowWidth();
}

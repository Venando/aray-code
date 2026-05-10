using System;
using System.Text;
using Spectre.Console;

namespace OpenClawPTT.Services;

/// <summary>
/// An IFormattedOutput wrapper that captures all Write/WriteLine output into a StringBuffer,
/// then pushes the complete result as a single StreamShell message when <see cref="FlushAsync"/>
/// is called. Useful for word-wrapped agent replies that need to appear in StreamShell.
/// </summary>
public sealed class StreamShellCapturingConsole : IFormattedOutput
{
    private readonly IStreamShellHost _shellHost;
    private readonly StringBuilder _buffer = new StringBuilder();

    public StreamShellCapturingConsole(IStreamShellHost shellHost)
    {
        _shellHost = shellHost;
    }

    /// <summary>
    /// Push the captured output to StreamShell and clear the buffer.
    /// The <paramref name="cyanPrefix"/> is rendered in cyan, and the captured body
    /// (everything after the prefix) is rendered in the default StreamShell color.
    /// </summary>
    public void FlushToStreamShell(string prefix)
    {
        if (_buffer.Length == 0)
            return;

        var text = _buffer.ToString().TrimEnd();

        // If after trimming we have nothing (e.g. only trailing whitespace from newline suffix),
        // send the prefix itself as a single message so the header still appears.
        if (string.IsNullOrEmpty(text))
        {
            _shellHost?.AddMessage(prefix);
            _buffer.Clear();
            return;
        }

        // First part of captured text is the prefix (already written via _consoleOutput.Write)
        // Split body into lines and add each as a default-color message
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            _shellHost?.AddMessage(i == 0 ? prefix + line : line);
        }

        _shellHost?.AddMessage("");

        _buffer.Clear();
    }

    // ── IFormattedOutput — capture all write operations ──

    public void Write(string text) => _buffer.Append(text);

    public void WriteLine() => _buffer.Append('\n');

    public int WindowWidth => ConsoleMetrics.GetWindowWidth();
}

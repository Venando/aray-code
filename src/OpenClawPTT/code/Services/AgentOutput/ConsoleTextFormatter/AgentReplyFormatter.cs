using Spectre.Console;

namespace OpenClawPTT;

/// <summary>
/// Formats streaming agent replies with word wrap and right margin indent.
/// Maintains state across delta chunks within a single reply.
/// </summary>
public sealed class AgentReplyFormatter : IAgentReplyFormatter
{
    private readonly int _rightMarginIndent;
    private readonly IFormattedOutput _output;
    private readonly TagStack _openMarkupTags = new();

    private WordWrapEngine _wordWrap = null!;
    private string _prefix = null!;
    private string _newlinePrefixLenght = null!;
    private bool _prefixAlreadyPrinted;
    private int _consoleWidth;

    /// <summary>
    /// Convenience constructor using default right-margin indent (10).
    /// </summary>
    public AgentReplyFormatter(string prefix, bool prefixAlreadyPrinted, IFormattedOutput output)
        : this(prefix, rightMarginIndent: 10, prefixAlreadyPrinted, output)
    {
    }

    /// <summary>
    /// Constructor with explicit word-wrap parameters.
    /// </summary>
    public AgentReplyFormatter(string prefix, int rightMarginIndent, bool prefixAlreadyPrinted, IFormattedOutput output)
    {
        _rightMarginIndent = rightMarginIndent;
        _output = output;
        Init(prefix, prefixAlreadyPrinted);
    }

    private void Init(string prefix, bool prefixAlreadyPrinted)
    {
        _prefix = Markup.Remove(prefix ?? string.Empty);
        _newlinePrefixLenght = new string(' ', _prefix.Length);
        _prefixAlreadyPrinted = prefixAlreadyPrinted;
        _consoleWidth = _output.WindowWidth > 0 ? _output.WindowWidth : 80;
        _wordWrap = new WordWrapEngine(GetAvailableWidth());
    }

    /// <summary>
    /// Calculates available text width: console width minus prefix width minus the right-edge margin.
    /// The right-edge margin reserves at least <see cref="_rightMarginIndent"/> characters,
    /// but scales up to 10% of console width on wider terminals for visual comfort.
    /// Falls back to half the console width when the nominal width is unusably small.
    /// </summary>
    private int GetAvailableWidth()
    {
        // Reserve the larger of the configured indent or 10% of console width
        // as the minimum right-edge margin — prevents text running to the very edge.
        int reservedRightMargin = Math.Max(_rightMarginIndent, (int)(_consoleWidth * 0.1));

        // After the first line, the prefix is replaced by an equal-width spacer.
        int usedPrefixWidth = _prefixAlreadyPrinted
            ? _newlinePrefixLenght.Length
            : _prefix.Length;

        int available = _consoleWidth - usedPrefixWidth - reservedRightMargin;
        return available > 0 ? available : _consoleWidth / 2;
    }

    public void Reconfigure(string prefix, bool prefixAlreadyPrinted = false)
    {
        Init(prefix, prefixAlreadyPrinted);
        _openMarkupTags.Clear();
    }

    /// <summary>
    /// Process a plain-text delta chunk and write formatted output with word-wrap.
    /// </summary>
    public void ProcessDelta(string delta)
    {
        foreach (char c in delta)
        {
            if (c == '\n')
            {
                // Skip leading newlines - prevents blank row before agent name
                if (_wordWrap.CurrentLineLength == 0 && _wordWrap.IsBufferEmpty)
                    continue;
                FlushAndWrite(_wordWrap.BufferLength);
                WriteNewLine();
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                FlushAndWrite(_wordWrap.BufferLength);
                if (_wordWrap.CurrentLineLength + 1 <= _wordWrap.AvailableWidth)
                {
                    _output.Write(c.ToString());
                    _wordWrap.RecordWritten(1);
                }
                else
                {
                    WriteNewLine();
                    _output.Write(c.ToString());
                    _wordWrap.RecordWritten(1);
                }
            }
            else
            {
                _wordWrap.AppendChar(c);
                if (_wordWrap.BufferLength > _wordWrap.AvailableWidth)
                {
                    int charsThatFit = _wordWrap.AvailableWidth - _wordWrap.CurrentLineLength;
                    if (charsThatFit > 0)
                    {
                        _output.Write(_wordWrap.FlushChars(charsThatFit));
                        _wordWrap.RecordWritten(charsThatFit);
                    }
                    if (_wordWrap.BufferLength > 0)
                    {
                        WriteNewLine();
                    }
                }
            }
        }
    }

    /// <summary>
    /// Process a pre-formatted markup string where [tag]…[/tag] sequences
    /// have zero visible width. Preserves markup tags in output.
    /// </summary>
    public void ProcessMarkupDelta(string markup)
    {
        bool insideTag = false;
        int visibleWordLen = 0;

        for (int i = 0; i < markup.Length; i++)
        {
            char c = markup[i];

            if (!insideTag && c == '[')
            {
                FlushAndWrite(visibleWordLen);
                visibleWordLen = 0;

                // Spectre uses [[ to represent a literal '['
                if (i + 1 < markup.Length && markup[i + 1] == '[')
                {
                    _wordWrap.AppendString("[[");
                    i++;
                    visibleWordLen++;
                    continue;
                }

                insideTag = true;
                _wordWrap.AppendChar(c);
                continue;
            }

            if (insideTag && c == ']')
            {
                insideTag = false;
                _wordWrap.AppendChar(c);

                int closePos = _wordWrap.BufferLength - 1;
                string bufferStr = _wordWrap.PeekBuffer();
                int openPos = bufferStr.LastIndexOf('[', closePos - 1);
                string tagContent = bufferStr.Substring(openPos + 1, closePos - openPos - 1);

                var validation = SpectreMarkupValidator.ValidateTagContent(tagContent);

                if (validation.ShouldEscape)
                {
                    _wordWrap.RemoveFromBuffer(_wordWrap.BufferLength - openPos);
                    _wordWrap.AppendString($"[[{tagContent}]]");
                    visibleWordLen += tagContent.Length + 4;
                    continue;
                }

                if (validation.NormalizedContent != tagContent)
                {
                    int toRemove = closePos - openPos + 1;
                    _wordWrap.RemoveFromBuffer(toRemove);
                    _wordWrap.AppendString($"[{validation.NormalizedContent}]");
                    tagContent = validation.NormalizedContent;
                }

                UpdateTagStack(tagContent);
                FlushAndWrite(0);
                continue;
            }

            if (insideTag)
            {
                _wordWrap.AppendChar(c);
                continue;
            }

            // Spectre uses ]] to represent a literal ']'
            if (c == ']' && i + 1 < markup.Length && markup[i + 1] == ']')
            {
                _wordWrap.AppendString("]]");
                i++;
                visibleWordLen++;
                continue;
            }

            if (c == '\n')
            {
                // Skip leading newlines - prevents blank row before agent name
                if (_wordWrap.CurrentLineLength == 0 && _wordWrap.IsBufferEmpty)
                    continue;
                FlushAndWrite(visibleWordLen);
                visibleWordLen = 0;
                WriteNewLine();
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                FlushAndWrite(visibleWordLen);
                visibleWordLen = 0;
                if (_wordWrap.CurrentLineLength + 1 <= _wordWrap.AvailableWidth)
                {
                    _output.Write(c.ToString());
                    _wordWrap.RecordWritten(1);
                }
                else
                {
                    WriteNewLine();
                    _output.Write(c.ToString());
                    _wordWrap.RecordWritten(1);
                }
                continue;
            }

            _wordWrap.AppendChar(c);
            visibleWordLen++;

            if (_wordWrap.WouldOverflow(visibleWordLen))
            {
                int remaining = _wordWrap.AvailableWidth - _wordWrap.CurrentLineLength;
                int charsToEmit = Math.Min(remaining, _wordWrap.BufferLength);

                if (charsToEmit > 0)
                {
                    _output.Write(_wordWrap.FlushChars(charsToEmit));
                    visibleWordLen = Math.Max(0, visibleWordLen - remaining);
                }

                if (_wordWrap.BufferLength > 0)
                {
                    string remainingBuf = _wordWrap.PeekBuffer();
                    int tagLen = remainingBuf.Length - visibleWordLen;

                    if (tagLen > 0 && remaining <= 0)
                    {
                        _output.Write(remainingBuf.Substring(0, tagLen));
                        _wordWrap.RemoveFromBuffer(tagLen);
                    }

                    WriteNewLine();
                }
            }
        }

        FlushAndWrite(visibleWordLen);
    }

    private void UpdateTagStack(string tagContent)
    {
        if (tagContent == "/")
        {
            if (_openMarkupTags.Count > 0)
                _openMarkupTags.Pop();
        }
        else if (tagContent.StartsWith("/"))
        {
            _openMarkupTags.PopMatching(tagContent.Substring(1));
        }
        else if (!string.IsNullOrEmpty(tagContent))
        {
            _openMarkupTags.Push(tagContent);
        }
    }

    public void Finish()
    {
        FlushAndWrite(_wordWrap.BufferLength);

        foreach (string _ in _openMarkupTags.AsEnumerable())
        {
            _output.Write("[/]");
        }
        _openMarkupTags.Clear();
        _output.WriteLine();
    }

    private void FlushAndWrite(int visibleLength)
    {
        if (_wordWrap.IsBufferEmpty) return;

        string content = _wordWrap.Flush();

        if (_wordWrap.CurrentLineLength + visibleLength <= _wordWrap.AvailableWidth)
        {
            _output.Write(content);
            _wordWrap.RecordWritten(visibleLength);
        }
        else
        {
            if (_wordWrap.CurrentLineLength > 0)
                WriteNewLine();
            _output.Write(content);
            _wordWrap.RecordWritten(visibleLength);
        }
    }

    private void WriteNewLine()
    {
        foreach (string tag in _openMarkupTags.AsEnumerable())
        {
            _output.Write("[/]");
        }

        _output.WriteLine();
        _output.Write(_newlinePrefixLenght);
        _wordWrap.RecordNewLine();

        foreach (string tag in _openMarkupTags.AsEnumerable())
        {
            _output.Write($"[{tag}]");
        }
    }
}

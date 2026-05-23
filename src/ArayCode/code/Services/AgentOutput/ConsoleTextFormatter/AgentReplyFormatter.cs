using Spectre.Console;

namespace ArayCode;

/// <summary>
/// Formats streaming agent replies with word wrap and right margin indent.
/// Maintains state across delta chunks within a single reply.
/// All width calculations use <see cref="CharacterWidth.GetDisplayWidth(char)"/>
/// to correctly handle CJK / fullwidth characters (visual width 2).
/// </summary>
public sealed class AgentReplyFormatter : IAgentReplyFormatter
{
    private readonly int _reservedRightMargin;
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
        : this(prefix, reservedRightMargin: 10, prefixAlreadyPrinted, output)
    {
    }

    /// <summary>
    /// Constructor with explicit word-wrap parameters.
    /// <param name="reservedRightMargin">Final right-edge margin in characters (already includes any console-width scaling).</param>
    /// </summary>
    public AgentReplyFormatter(string prefix, int reservedRightMargin, bool prefixAlreadyPrinted, IFormattedOutput output)
    {
        _reservedRightMargin = reservedRightMargin;
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
    /// Calculates available text width: console width minus prefix visual width minus the pre-computed
    /// right-edge margin (<see cref="_reservedRightMargin"/>).
    /// Falls back to half the console width when the nominal width is unusably small.
    /// </summary>
    private int GetAvailableWidth()
    {
        int usedPrefixWidth = _prefixAlreadyPrinted
            ? CharacterWidth.GetDisplayWidth(_newlinePrefixLenght)
            : CharacterWidth.GetDisplayWidth(_prefix);

        int available = _consoleWidth - usedPrefixWidth - _reservedRightMargin;
        return available > 0 ? available : _consoleWidth / 2;
    }

    public void Reconfigure(string prefix, bool prefixAlreadyPrinted = false)
    {
        Init(prefix, prefixAlreadyPrinted);
        _openMarkupTags.Clear();
    }

    /// <summary>
    /// Process a plain-text delta chunk and write formatted output with word-wrap.
    /// Uses <see cref="CharacterWidth.GetDisplayWidth(char)"/> to correctly account
    /// for CJK / fullwidth characters.
    /// </summary>
    public void ProcessDelta(string delta)
    {
        foreach (char c in delta)
        {
            int cw = CharacterWidth.GetDisplayWidth(c);

            if (c == '\n')
            {
                // Skip leading newlines - prevents blank row before agent name
                if (_wordWrap.CurrentLineLength == 0 && _wordWrap.IsBufferEmpty)
                    continue;
                FlushAndWrite(_wordWrap.GetBufferVisualWidth());
                WriteNewLine();
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                FlushAndWrite(_wordWrap.GetBufferVisualWidth());
                if (_wordWrap.CurrentLineLength + cw <= _wordWrap.AvailableWidth)
                {
                    _output.Write(c.ToString());
                    _wordWrap.RecordWritten(cw);
                }
                else
                {
                    WriteNewLine();
                    _output.Write(c.ToString());
                    _wordWrap.RecordWritten(cw);
                }
            }
            else
            {
                _wordWrap.AppendChar(c);
                int bufVisualWidth = _wordWrap.GetBufferVisualWidth();
                if (_wordWrap.CurrentLineLength + bufVisualWidth > _wordWrap.AvailableWidth)
                {
                    // Word/run too long for the remaining space on this line.
                    if (_wordWrap.CurrentLineLength > 0)
                    {
                        // Line has content — move the entire buffer (the current word)
                        // to the next line so it's not cut in half.
                        WriteNewLine();
                    }
                    else
                    {
                        // Line is empty — word is longer than the whole line width.
                        // Must break it at the column boundary.
                        string lineFit = _wordWrap.FlushCharsByVisualWidth(_wordWrap.AvailableWidth);
                        if (lineFit.Length > 0)
                        {
                            int fitWidth = CharacterWidth.GetDisplayWidth(lineFit);
                            _output.Write(lineFit);
                            _wordWrap.RecordWritten(fitWidth);
                        }
                        if (_wordWrap.BufferLength > 0)
                        {
                            WriteNewLine();
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Process a pre-formatted markup string where [tag]…[/tag] sequences
    /// have zero visible width. Preserves markup tags in output.
    /// Uses <see cref="CharacterWidth.GetDisplayWidth(char)"/> for visual width tracking.
    /// </summary>
    public void ProcessMarkupDelta(string markup)
    {
        bool insideTag = false;
        int visibleWordWidth = 0;
        int nonTagCharsSinceLastWhitespace = 0;

        for (int i = 0; i < markup.Length; i++)
        {
            char c = markup[i];

            if (!insideTag && c == '[')
            {
                FlushAndWrite(visibleWordWidth);
                visibleWordWidth = 0;
                nonTagCharsSinceLastWhitespace = 0;

                // Spectre uses [[ to represent a literal '['
                if (i + 1 < markup.Length && markup[i + 1] == '[')
                {
                    _wordWrap.AppendString("[[");
                    i++;
                    int d1 = CharacterWidth.GetDisplayWidth('[');
                    visibleWordWidth += d1 + CharacterWidth.GetDisplayWidth('[');
                    nonTagCharsSinceLastWhitespace += 2;
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
                    visibleWordWidth += CharacterWidth.GetDisplayWidth(tagContent) + 4;
                    nonTagCharsSinceLastWhitespace += tagContent.Length + 4;
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
                visibleWordWidth += CharacterWidth.GetDisplayWidth(']') + CharacterWidth.GetDisplayWidth(']');
                nonTagCharsSinceLastWhitespace += 2;
                continue;
            }

            int cw = CharacterWidth.GetDisplayWidth(c);

            if (c == '\n')
            {
                // Skip leading newlines - prevents blank row before agent name
                if (_wordWrap.CurrentLineLength == 0 && _wordWrap.IsBufferEmpty)
                    continue;
                FlushAndWrite(visibleWordWidth);
                visibleWordWidth = 0;
                nonTagCharsSinceLastWhitespace = 0;
                WriteNewLine();
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                FlushAndWrite(visibleWordWidth);
                visibleWordWidth = 0;
                nonTagCharsSinceLastWhitespace = 0;
                if (_wordWrap.CurrentLineLength + cw <= _wordWrap.AvailableWidth)
                {
                    _output.Write(c.ToString());
                    _wordWrap.RecordWritten(cw);
                }
                else
                {
                    WriteNewLine();
                    _output.Write(c.ToString());
                    _wordWrap.RecordWritten(cw);
                }
                continue;
            }

            _wordWrap.AppendChar(c);
            visibleWordWidth += cw;
            nonTagCharsSinceLastWhitespace++;

            if (_wordWrap.WouldOverflow(visibleWordWidth))
            {
                if (_wordWrap.CurrentLineLength > 0)
                {
                    // Line has content — move the entire buffer (visible word + any
                    // inline tags) to the next line so it's not cut in half.
                    WriteNewLine();
                }
                else
                {
                    // Line is empty — this word is longer than the whole line width.
                    // Must break it at the column boundary.
                    int remaining = _wordWrap.AvailableWidth;
                    string fit = _wordWrap.FlushCharsByVisualWidth(remaining);
                    if (fit.Length > 0)
                    {
                        int fitWidth = CharacterWidth.GetDisplayWidth(fit);
                        _output.Write(fit);
                        visibleWordWidth = Math.Max(0, visibleWordWidth - fitWidth);
                        _wordWrap.RecordWritten(fitWidth);
                        nonTagCharsSinceLastWhitespace = 0;
                    }

                    if (_wordWrap.BufferLength > 0)
                    {
                        string remainingBuf = _wordWrap.PeekBuffer();
                        int tagLen = remainingBuf.Length - visibleWordWidth;
                        if (tagLen > 0)
                        {
                            _output.Write(remainingBuf.Substring(0, tagLen));
                            _wordWrap.RemoveFromBuffer(tagLen);
                            nonTagCharsSinceLastWhitespace = 0;
                        }
                        WriteNewLine();
                    }
                }
            }
        }

        FlushAndWrite(visibleWordWidth);
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
        FlushAndWrite(_wordWrap.GetBufferVisualWidth());

        foreach (string _ in _openMarkupTags.AsEnumerable())
        {
            _output.Write("[/]");
        }
        _openMarkupTags.Clear();
        _output.WriteLine();
    }

    private void FlushAndWrite(int visibleWidth)
    {
        if (_wordWrap.IsBufferEmpty) return;

        string content = _wordWrap.Flush();

        if (_wordWrap.CurrentLineLength + visibleWidth <= _wordWrap.AvailableWidth)
        {
            _output.Write(content);
            _wordWrap.RecordWritten(visibleWidth);
        }
        else
        {
            if (_wordWrap.CurrentLineLength > 0)
                WriteNewLine();
            _output.Write(content);
            _wordWrap.RecordWritten(visibleWidth);
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

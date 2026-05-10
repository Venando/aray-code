using System.Text;

namespace OpenClawPTT;

/// <summary>
/// Pure word-wrapping engine that tracks current line length and available width.
/// Accumulates characters into a buffer and determines when wrapping is needed.
/// All width values are in visual display columns (CJK = 2, ASCII = 1).
/// </summary>
public sealed class WordWrapEngine
{
    private readonly StringBuilder _buffer = new();
    private int _currentVisualLineLength;
    private readonly int _availableVisualWidth;

    /// <summary>
    /// Creates a new word-wrap engine with the specified available width in visual columns.
    /// </summary>
    public WordWrapEngine(int availableWidth)
    {
        _availableVisualWidth = availableWidth > 0 ? availableWidth : 80;
    }

    /// <summary>
    /// Gets the current line length (visual display columns on current line).
    /// </summary>
    public int CurrentLineLength => _currentVisualLineLength;

    /// <summary>
    /// Gets the available width in visual columns for each line.
    /// </summary>
    public int AvailableWidth => _availableVisualWidth;

    /// <summary>
    /// Gets the number of characters in the accumulated buffer.
    /// </summary>
    public int BufferLength => _buffer.Length;

    /// <summary>
    /// Gets whether the buffer is empty.
    /// </summary>
    public bool IsBufferEmpty => _buffer.Length == 0;

    /// <summary>
    /// Appends a single character to the buffer.
    /// </summary>
    public void AppendChar(char c) => _buffer.Append(c);

    /// <summary>
    /// Appends a string to the buffer.
    /// </summary>
    public void AppendString(string s) => _buffer.Append(s);

    /// <summary>
    /// Returns true if adding <paramref name="visualLength"/> visual columns
    /// would exceed the available width.
    /// </summary>
    public bool NeedsWrap(int visualLength)
    {
        return _currentVisualLineLength + visualLength > _availableVisualWidth;
    }

    /// <summary>
    /// Calculates how many visual columns would fit on the current line
    /// given <paramref name="visualLength"/> that needs to be added.
    /// </summary>
    public int CalculateFitLength(int visualLength)
    {
        int remaining = _availableVisualWidth - _currentVisualLineLength;
        return Math.Min(remaining, visualLength);
    }

    /// <summary>
    /// Returns true if the current word (based on visual width) is too long
    /// for the remaining space on the current line.
    /// </summary>
    public bool WouldOverflow(int visualWordWidth)
    {
        return visualWordWidth > _availableVisualWidth - _currentVisualLineLength;
    }

    /// <summary>
    /// Flushes the buffer and returns its contents.
    /// Does not reset line length - use <see cref="RecordWritten"/> to update.
    /// </summary>
    public string Flush()
    {
        if (_buffer.Length == 0)
            return string.Empty;

        string content = _buffer.ToString();
        _buffer.Clear();
        return content;
    }

    /// <summary>
    /// Flushes a specific number of characters from the start of the buffer.
    /// </summary>
    public string FlushChars(int charCount)
    {
        if (_buffer.Length == 0 || charCount <= 0)
            return string.Empty;

        int toFlush = Math.Min(charCount, _buffer.Length);
        string content = _buffer.ToString(0, toFlush);
        _buffer.Remove(0, toFlush);
        return content;
    }

    /// <summary>
    /// Computes the visual display width of the buffer content.
    /// </summary>
    public int GetBufferVisualWidth()
    {
        int width = 0;
        for (int i = 0; i < _buffer.Length; i++)
            width += CharacterWidth.GetDisplayWidth(_buffer[i]);
        return width;
    }

    /// <summary>
    /// Computes the visual display width of the first <paramref name="charCount"/> characters
    /// in the buffer.
    /// </summary>
    public int GetBufferVisualWidth(int charCount)
    {
        int limit = Math.Min(charCount, _buffer.Length);
        int width = 0;
        for (int i = 0; i < limit; i++)
            width += CharacterWidth.GetDisplayWidth(_buffer[i]);
        return width;
    }

    /// <summary>
    /// Flushes the maximum number of characters from the buffer whose combined
    /// visual width does not exceed <paramref name="maxVisualWidth"/>.
    /// Returns the flushed string.
    /// </summary>
    public string FlushCharsByVisualWidth(int maxVisualWidth)
    {
        if (_buffer.Length == 0 || maxVisualWidth <= 0)
            return string.Empty;

        int visualWidth = 0;
        int charCount = 0;
        for (int i = 0; i < _buffer.Length; i++)
        {
            int cw = CharacterWidth.GetDisplayWidth(_buffer[i]);
            if (visualWidth + cw > maxVisualWidth)
                break;
            visualWidth += cw;
            charCount++;
        }

        if (charCount == 0)
            return string.Empty;

        string content = _buffer.ToString(0, charCount);
        _buffer.Remove(0, charCount);
        return content;
    }

    /// <summary>
    /// Finds the index of the last whitespace character in the buffer.
    /// Returns -1 if no whitespace found.
    /// </summary>
    public int FindLastWhitespace()
    {
        for (int i = _buffer.Length - 1; i >= 0; i--)
        {
            if (char.IsWhiteSpace(_buffer[i]))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Records that <paramref name="visualLength"/> visual columns
    /// were written to the output, updating the current line length.
    /// </summary>
    public void RecordWritten(int visualLength)
    {
        _currentVisualLineLength += visualLength;
    }

    /// <summary>
    /// Records that a new line was started, resetting the current line length to zero.
    /// </summary>
    public void RecordNewLine()
    {
        _currentVisualLineLength = 0;
    }

    /// <summary>
    /// Sets the current line length to a specific visual width value (used after prefix output).
    /// </summary>
    public void SetLineLength(int length)
    {
        _currentVisualLineLength = Math.Max(0, length);
    }

    /// <summary>
    /// Clears the buffer without returning its contents.
    /// </summary>
    public void ClearBuffer() => _buffer.Clear();

    /// <summary>
    /// Gets the current buffer contents without clearing it.
    /// </summary>
    public string PeekBuffer() => _buffer.ToString();

    /// <summary>
    /// Gets a substring of the buffer without modifying it.
    /// </summary>
    public string PeekBufferSubstring(int startIndex, int length)
    {
        if (startIndex >= _buffer.Length)
            return string.Empty;

        int availableLength = Math.Min(length, _buffer.Length - startIndex);
        return _buffer.ToString(startIndex, availableLength);
    }

    /// <summary>
    /// Removes characters from the start of the buffer.
    /// </summary>
    public void RemoveFromBuffer(int charCount)
    {
        if (charCount > 0 && _buffer.Length > 0)
        {
            _buffer.Remove(0, Math.Min(charCount, _buffer.Length));
        }
    }
}

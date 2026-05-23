namespace ArayCode;

/// <summary>
/// Encapsulates a stack of open Spectre.Console markup tags.
/// Tracks tags that have been opened but not yet closed for proper
/// re-emission during word-wrap line breaks.
/// </summary>
public sealed class TagStack
{
    private readonly Stack<string> _openTags = new();

    /// <summary>
    /// Gets the number of open tags in the stack.
    /// </summary>
    public int Count => _openTags.Count;

    /// <summary>
    /// Pushes an opening tag onto the stack.
    /// </summary>
    public void Push(string tag) => _openTags.Push(tag);

    /// <summary>
    /// Pops the most recent tag from the stack.
    /// </summary>
    public string Pop() => _openTags.Pop();

    /// <summary>
    /// Pops tags from the stack until a matching tag name is found.
    /// Returns true if a matching tag was found and removed.
    /// Non-matching tags are preserved on the stack in their original order.
    /// </summary>
    public bool PopMatching(string tagName)
    {
        if (string.IsNullOrEmpty(tagName) || _openTags.Count == 0)
            return false;

        var tempStack = new Stack<string>();
        bool found = false;

        while (_openTags.Count > 0)
        {
            string top = _openTags.Pop();
            if (string.Equals(top, tagName, StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                break;
            }
            tempStack.Push(top);
        }

        // Restore non-matching tags
        while (tempStack.Count > 0)
        {
            _openTags.Push(tempStack.Pop());
        }

        return found;
    }

    /// <summary>
    /// Returns a copy of all open tags in stack order (most recent last).
    /// </summary>
    public string[] GetOpenTags() => _openTags.Reverse().ToArray();

    /// <summary>
    /// Clears all tags from the stack.
    /// </summary>
    public void Clear() => _openTags.Clear();

    /// <summary>
    /// Returns an enumerable of tags for iteration (in LIFO order).
    /// </summary>
    public IEnumerable<string> AsEnumerable() => _openTags;
}

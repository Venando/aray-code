namespace ArayCode.Services;

/// <summary>
/// Abstraction for tool output operations, enabling testability and
/// alternate render targets (e.g. string builders for testing).
///
/// Color/style is specified as a Spectre.Console markup style string
/// (e.g. "grey", "bold cyan", "default on gray15").
/// Null or empty uses the terminal default.
/// </summary>
public interface IToolOutput
{
    void Start(string prefix);
    void Print(string text, string? style = null);
    void PrintLine(string text, string? style = null);
    void PrintTruncated(string text, string continuationPrefix, int rightMarginIndent, string? style = null, int maxRows = 4);
    void PrintMarkup(string markup);
    void Finish();
    void Flush();
}

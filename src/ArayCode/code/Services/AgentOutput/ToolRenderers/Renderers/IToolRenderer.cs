using System.Text.Json;

namespace ArayCode.Services;

/// <summary>
/// Renders a specific tool's arguments to console output.
/// </summary>
public interface IToolRenderer
{
    /// <summary>
    /// The tool name this renderer handles (e.g. "read", "write").
    /// </summary>
    string ToolName { get; }

    /// <summary>
    /// Render the tool arguments to output.
    /// </summary>
    void Render(JsonElement args, int rightMarginIndent);
}

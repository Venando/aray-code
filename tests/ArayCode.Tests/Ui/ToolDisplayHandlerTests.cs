using ArayCode.Services;
using Xunit;

namespace ArayCode.Tests;

/// <summary>
/// Tests for ToolDisplayHandler edge cases.
/// </summary>
public class ToolDisplayHandlerTests
{
    // ─── Renderer architecture tests ───────────────────────────────────────────

    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    [InlineData("edit")]
    [InlineData("exec")]
    [InlineData("web_fetch")]
    [InlineData("sessions_list")]
    [InlineData("session_status")]
    [InlineData("memory_search")]
    [InlineData("memory_get")]
    [InlineData("subagents")]
    [InlineData("sessions_spawn")]
    [InlineData("process")]
    [InlineData("web_search")]
    [InlineData("image_generate")]
    public void Handle_KnownTool_DoesNotThrow(string toolName)
    {
        var handler = new ToolDisplayHandler(rightMarginIndent: 10);
        handler.Handle(toolName, "{\"key\":\"value\"}");
    }

    [Fact]
    public void Handle_UnknownTool_DoesNotThrow()
    {
        var handler = new ToolDisplayHandler(rightMarginIndent: 10);
        handler.Handle("totally_unknown_tool", "{\"key\":\"value\"}");
    }

    [Fact]
    public void Handle_MalformedJson_UnknownTool_DoesNotThrow()
    {
        var handler = new ToolDisplayHandler(rightMarginIndent: 10);
        handler.Handle("unknown_tool", "{not json");
    }

    [Fact]
    public void Handle_AllRenderersWithEmptyArgs_DoNotThrow()
    {
        var handler = new ToolDisplayHandler(rightMarginIndent: 10);
        // Each of these should not throw even with empty/missing arguments
        handler.Handle("read", "{}");
        handler.Handle("write", "{}");
        handler.Handle("edit", "{}");
        handler.Handle("exec", "{}");
        handler.Handle("web_fetch", "{}");
        handler.Handle("sessions_list", "{}");
        handler.Handle("session_status", "{}");
        handler.Handle("memory_search", "{}");
        handler.Handle("memory_get", "{}");
        handler.Handle("subagents", "{}");
        handler.Handle("sessions_spawn", "{}");
        handler.Handle("process", "{}");
    }
}

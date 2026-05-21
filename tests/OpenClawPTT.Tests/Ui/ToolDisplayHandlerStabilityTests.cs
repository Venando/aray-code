using System;
using System.Collections.Generic;
using System.Text.Json;
using OpenClawPTT.Services;
using Xunit;

namespace OpenClawPTT.Tests;

/// <summary>
/// Stability and error-path tests for ToolDisplayHandler.
/// Covers null/empty inputs, edge-case JSON, and duplicate renderer guard.
/// </summary>
public class ToolDisplayHandlerStabilityTests
{
    private sealed class FakeToolOutput : IToolOutput
    {
        public void Start(string prefix) { }
        public void Print(string text, string? style = null) { }
        public void PrintLine(string text, string? style = null) { }
        public void PrintTruncated(string text, string continuationPrefix, int rightMarginIndent, string? style = null, int maxRows = 4) { }
        public void PrintMarkup(string markup) { }
        public void Finish() { }
        public void Flush() { }
        public void ResetColor() { }
    }

    private sealed class DummyRenderer : IToolRenderer
    {
        public string ToolName => "dummy";
        public void Render(JsonElement args, int rightMarginIndent) { }
    }

    private sealed class AnotherDummyRenderer : IToolRenderer
    {
        public string ToolName => "dummy"; // intentionally duplicate — should cause ArgumentException
        public void Render(JsonElement args, int rightMarginIndent) { }
    }

    // ─── Test 1: Handle(null) should not crash ─────────────────────────────────

    [Fact]
    public void Handle_NullToolName_DoesNotThrow()
    {
        var handler = new ToolDisplayHandler(rightMarginIndent: 10);
        // Must not throw NullReferenceException from toolName.Split('_')
        var ex = Record.Exception(() => handler.Handle(null!, "{\"key\":1}"));
        Assert.Null(ex);
    }

    // ─── Test 2: Handle("") should not crash ───────────────────────────────────

    [Fact]
    public void Handle_EmptyToolName_DoesNotThrow()
    {
        var handler = new ToolDisplayHandler(rightMarginIndent: 10);
        // Must not throw IndexOutOfRangeException from w[0] on empty string segment
        var ex = Record.Exception(() => handler.Handle("", "{\"key\":1}"));
        Assert.Null(ex);
    }

    // ─── Test 3: Duplicate tool renderer names → ArgumentException ────────────

    [Fact]
    public void Constructor_WithDuplicateRendererNames_ThrowsArgumentException()
    {
        var renderers = new List<IToolRenderer>
        {
            new DummyRenderer(),
            new AnotherDummyRenderer(),
        };
        // The .ToDictionary call inside the constructor will throw because
        // both DummyRenderer and AnotherDummyRenderer report ToolName = "dummy"
        var ex = Record.Exception(() => new ToolDisplayHandler(
            new FakeToolOutput(),
            renderers,
            rightMarginIndent: 10));
        Assert.IsType<ArgumentException>(ex);
    }

    // ─── Test 4: Valid JSON with non-string leaf values → should not crash ─────

    [Fact]
    public void Handle_NonStringLeafValue_DoesNotThrow()
    {
        var handler = new ToolDisplayHandler(rightMarginIndent: 10);
        // {"file": 123} — numeric value where a string is expected.
        // ReadToolRenderer calls GetString() which returns null for numbers. Should not throw.
        var ex = Record.Exception(() => handler.Handle("read", "{\"file\":123}"));
        Assert.Null(ex);
    }

    // ─── Test 5: Tool name with special characters → renders without crash ─────

    [Fact]
    public void Handle_ToolNameWithSpecialCharacters_DoesNotThrow()
    {
        var handler = new ToolDisplayHandler(rightMarginIndent: 10);
        // Hyphens, dots, and Unicode characters in the tool name.
        // Must not throw on Split('_') or string slicing.
        var ex = Record.Exception(() => handler.Handle("tool-name.with-special🔧", "{\"key\":\"value\"}"));
        Assert.Null(ex);
    }
}

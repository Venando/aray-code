using System.Text.Json;

namespace OpenClawPTT.Services;

/// <summary>
/// Formats tool calls into one-line status descriptions for the bottom panel.
/// Same registry pattern as <see cref="ToolDisplayHandler"/> but returns
/// short strings instead of rendering full console output.
/// </summary>
public sealed class AgentActivityFormatter
{
    public static readonly AgentActivityFormatter Default = new();

    private readonly Dictionary<string, IAgentActivityRenderer> _renderers;

    public AgentActivityFormatter()
    {
        _renderers = BuildRenderers()
            .Where(r => !string.IsNullOrEmpty(r.ToolName))
            .ToDictionary(r => r.ToolName, r => r, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<IAgentActivityRenderer> BuildRenderers()
    {
        yield return new ReadActivityRenderer();
        yield return new EditActivityRenderer();
        yield return new WriteActivityRenderer();
        yield return new ExecActivityRenderer();
        yield return new WebFetchActivityRenderer();
    }

    public string FormatTool(string toolName, string? argsJson)
    {
        JsonDocument? doc = null;
        JsonElement? args = null;
        if (argsJson is not null)
        {
            try { doc = JsonDocument.Parse(argsJson); args = doc.RootElement; }
            catch { doc?.Dispose(); doc = null; }
        }

        string result;
        if (_renderers.TryGetValue(toolName, out var renderer))
            result = renderer.Render(args);
        else
            result = $"Executing {toolName}";

        doc?.Dispose();
        return result;
    }

    public string FormatAssistantMessage(AssistantMessageEvent? msg)
    {
        if (msg is null) return "Sent a message";
        return msg.StopReason switch
        {
            "toolUse" => "Calling tools",
            "stop" => "Finished",
            "error" => "Error",
            "aborted" => "Aborted",
            _ => "Sent a message"
        };
    }

    public string FormatUserMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Sent a message";
        var trimmed = text.Trim();
        if (trimmed.Length > 60) return trimmed[..57] + "…";
        return trimmed;
    }
}

// ── Renderers ────────────────────────────────────────────────────────────────

internal sealed class ReadActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "read";
    public string Render(JsonElement? args)
    {
        var path = Helpers.GetString(args, "path");
        return path is not null ? $"Reading {Helpers.ShortenPath(path)}" : "Reading file";
    }
}

internal sealed class EditActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "edit";
    public string Render(JsonElement? args)
    {
        var path = Helpers.GetString(args, "path");
        return path is not null ? $"Editing {Helpers.ShortenPath(path)}" : "Editing file";
    }
}

internal sealed class WriteActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "write";
    public string Render(JsonElement? args)
    {
        var path = Helpers.GetString(args, "path");
        return path is not null ? $"Writing {Helpers.ShortenPath(path)}" : "Writing file";
    }
}

internal sealed class ExecActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "exec";
    public string Render(JsonElement? args)
    {
        var cmd = Helpers.GetString(args, "command");
        if (cmd is null) return "Running command";
        var firstLine = cmd.Split('\n')[0].Trim();
        if (firstLine.Length > 60) return "Running " + firstLine[..57] + "…";
        return "Running " + firstLine;
    }
}

internal sealed class WebFetchActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "web_fetch";
    public string Render(JsonElement? args)
    {
        var url = Helpers.GetString(args, "url");
        if (url is null) return "Fetching URL";
        var display = url.Replace("https://", "").Replace("http://", "");
        if (display.Length > 50) display = display[..47] + "…";
        return $"Fetching {display}";
    }
}

// ── Helpers ──────────────────────────────────────────────────────────────────

internal static class Helpers
{
    public static string? GetString(JsonElement? el, string key)
    {
        if (el is not { } e) return null;
        if (e.ValueKind == JsonValueKind.Object
            && e.TryGetProperty(key, out var v)
            && v.ValueKind == JsonValueKind.String)
            return v.GetString();
        return null;
    }

    public static string ShortenPath(string path)
    {
        var parts = path.Replace('\\', '/').Split('/');
        if (parts.Length <= 2) return path;
        return "…/" + string.Join("/", parts[^2..]);
    }
}

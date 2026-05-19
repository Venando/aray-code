using System.Text;
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

    private readonly TtsContentFilter.SanitizerOptions _sanitizerOptions;

    private readonly StringBuilder _stringBuilder = new();

    public AgentActivityFormatter()
    {
        _renderers = BuildRenderers()
            .Where(r => !string.IsNullOrEmpty(r.ToolName))
            .ToDictionary(r => r.ToolName, r => r, StringComparer.OrdinalIgnoreCase);

        _sanitizerOptions = new TtsContentFilter.SanitizerOptions()
        {
            MaxLength = 300
        };
    }

    private static IEnumerable<IAgentActivityRenderer> BuildRenderers()
    {
        yield return new ReadActivityRenderer();
        yield return new EditActivityRenderer();
        yield return new WriteActivityRenderer();
        yield return new ExecActivityRenderer();
        yield return new WebFetchActivityRenderer();
    }

    public string FormatTool(string toolName, string? arguments)
    {

        string displayName = string.Join(" ", toolName.Split('_').Select(w => char.ToUpper(w[0]) + w[1..]));

        if (string.IsNullOrWhiteSpace(arguments))
        {
            return $"Executing {displayName}";
        }

        try
        {
            using var doc = JsonDocument.Parse(arguments);
            if (_renderers.TryGetValue(toolName, out var renderer))
            {
                return renderer.Render(doc.RootElement);
            }
            else
            {
                return RenderKvpProperties(_stringBuilder, displayName, doc.RootElement);
            }
        }
        catch
        {
            return $"Executing {displayName}";
        }
    }

    private static string RenderKvpProperties(StringBuilder sb, string displayName, JsonElement args)
    {
        sb.Clear();
        sb.Append(displayName);
        sb.Append(' ');
        bool first = true;
        foreach (var prop in args.EnumerateObject())
        {
            if (first)
            {
                sb.Append(ToolRendererBase.GetValueString(prop.Value));
                first = false;
            }
            else
            {
                sb.Append($", ");
                sb.Append(prop.Name);
                sb.Append($": ");
                sb.Append(ToolRendererBase.GetValueString(prop.Value));
            }
        }
        return sb.ToString();
    }

    public string FormatAssistantMessage(string? message)
    {
        if (message is null) return "Sent a message";

        string formatText = TtsContentFilter.SanitizeForTts(message, _sanitizerOptions);

        return formatText.Replace('\n', ' ').Replace("  ", " ");
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
    private readonly StringBuilder _sb = new();

    public string ToolName => "read";

    public string Render(JsonElement args)
    {
        _sb.Clear();
        _sb.Append("Reading ");
        if (args.TryGetProperty("file", out var fileProp) || args.TryGetProperty("path", out fileProp))
        {
            string displayPath = FilePathDisplayHelper.FormatDisplayPath(fileProp.GetString() ?? "");
            _sb.Append(displayPath);
        }
        if (args.TryGetProperty("offset", out var offsetProp) &&
            args.TryGetProperty("limit", out var limitProp))
        {
            int offset = offsetProp.GetInt32();
            int limit = limitProp.GetInt32();
            _sb.Append($" (lines {offset}-{offset + limit - 1})");
        }
        else if (args.TryGetProperty("limit", out var limitProp2))
        {
            _sb.Append($" (lines 1-{limitProp2.GetInt32()})");
        }
        return _sb.ToString();
    }
}

internal sealed class EditActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "edit";
    public string Render(JsonElement args)
    {

        var path = Helpers.GetString(args, "path");
        return path is not null ? $"Editing {Helpers.ShortenPath(path)}" : "Editing file";
    }
}

internal sealed class WriteActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "write";
    public string Render(JsonElement args)
    {
        var path = Helpers.GetString(args, "path");
        return path is not null ? $"Writing {Helpers.ShortenPath(path)}" : "Writing file";
    }
}

internal sealed class ExecActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "exec";
    public string Render(JsonElement args)
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
    public string Render(JsonElement args)
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

using System.Text;
using System.Text.Json;

namespace ArayCode.Services;

/// <summary>
/// Thin registry/dispatcher for agent activity renderers.
/// Similar to <see cref="ToolDisplayHandler"/> but returns one-line strings
/// instead of rendering full console output.
/// </summary>
public sealed class AgentActivityFormatter
{
    public static readonly AgentActivityFormatter Default = new();

    private readonly Dictionary<string, IAgentActivityRenderer> _renderers;
    private readonly TtsContentFilter.SanitizerOptions _sanitizerOptions;

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
        yield return new WebSearchActivityRenderer();
        yield return new SessionsListActivityRenderer();
        yield return new SessionStatusActivityRenderer();
        yield return new MemorySearchActivityRenderer();
        yield return new MemoryGetActivityRenderer();
        yield return new SubagentsActivityRenderer();
        yield return new SessionsSpawnActivityRenderer();
        yield return new UpdatePlanActivityRenderer();
        yield return new ImageGenerateActivityRenderer();
        yield return new ProcessActivityRenderer();
    }

    public string FormatTool(string toolName, string? arguments, string? meta = null)
    {
        string displayName = AgentActivityRendererHelpers.FormatDisplayName(toolName);

        // When arguments are missing but meta is present, synthesize JSON args
        // so the dedicated renderer can handle it naturally.
        if (string.IsNullOrWhiteSpace(arguments) && !string.IsNullOrWhiteSpace(meta))
        {
            var synthetic = BuildSyntheticArgs(toolName, meta);
            if (synthetic is not null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(synthetic);
                    if (_renderers.TryGetValue(toolName, out var renderer))
                        return renderer.Render(doc.RootElement);
                }
                catch { /* fall through to generic fallback */ }
            }
        }

        if (string.IsNullOrWhiteSpace(arguments))
        {
            return FormatToolFallback(displayName, meta);
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
                return RenderKvpFallback(displayName, doc.RootElement);
            }
        }
        catch
        {
            return FormatToolFallback(displayName, meta);
        }
    }

    /// <summary>
    /// Builds a synthetic JSON args object from meta when the real args are not available.
    /// Maps each tool to the property name its renderer expects.
    /// </summary>
    private static string? BuildSyntheticArgs(string toolName, string meta)
    {
        var (property, value) = toolName.ToLowerInvariant() switch
        {
            "read" => ("file",
                meta.StartsWith("from ", StringComparison.OrdinalIgnoreCase)
                    ? meta.Substring(5)
                    : meta),
            "exec" => ("command", meta),
            "web_fetch" => ("url", meta),
            "web_search" => ("query", meta),
            "memory_search" => ("query", meta),
            "memory_get" => ("path", meta),
            "write" => ("path", meta),
            "edit" => ("path", meta),
            _ => ((string?)null, null)
        };

        if (property is null || value is null)
            return null;

        // Minimal JSON escaping — meta is trusted from gateway, but escape quotes/newlines
        var escaped = value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");

        return $"{{\"{property}\":\"{escaped}\"}}";
    }

    private static string FormatToolFallback(string displayName, string? meta)
    {
        if (!string.IsNullOrWhiteSpace(meta))
            return $"Executing {displayName} — {meta}";

        return $"Executing {displayName}";
    }

    private static string RenderKvpFallback(string displayName, JsonElement args)
    {
        var sb = new StringBuilder();
        sb.Append(displayName);
        sb.Append(' ');

        bool first = true;
        foreach (var prop in args.EnumerateObject())
        {
            if (first)
            {
                sb.Append(AgentActivityRendererHelpers.Truncate(
                    ToolRendererBase.GetValueString(prop.Value), 50));
                first = false;
                break;  // Only show first property for brevity
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

    public string FormatAssistantThoughts(string? message)
    {
        if (message is null) return "Thinking...";

        string formatText = TtsContentFilter.SanitizeForTts(message, _sanitizerOptions);

        return "Thinking..." + formatText.Replace('\n', ' ').Replace("  ", " ");
    }
}

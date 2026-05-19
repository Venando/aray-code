using System.Text;
using System.Text.Json;

namespace OpenClawPTT.Services;

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

    public string FormatTool(string toolName, string? arguments)
    {
        string displayName = AgentActivityRendererHelpers.FormatDisplayName(toolName);

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
                return RenderKvpFallback(displayName, doc.RootElement);
            }
        }
        catch
        {
            return $"Executing {displayName}";
        }
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

    public string FormatUserMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Sent a message";
        var trimmed = text.Trim();
        if (trimmed.Length > 60) return trimmed[..57] + "…";
        return trimmed;
    }
}

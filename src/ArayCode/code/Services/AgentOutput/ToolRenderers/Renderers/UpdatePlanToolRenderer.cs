using System.Text.Json;

namespace ArayCode.Services;

/// <summary>
/// Renders the update_plan tool — displays plan steps with status indicators
/// using Spectre.Console markup for a clear, visually appealing layout.
/// </summary>
public sealed class UpdatePlanToolRenderer : ToolRendererBase
{
    private const int StatusWidth = 14;

    public UpdatePlanToolRenderer(IToolOutput output) : base(output)
    {
    }

    public override string ToolName => "update_plan";

    public override void Render(JsonElement args, int rightMarginIndent)
    {
        // Optional explanation
        if (args.TryGetProperty("explanation", out var explanationProp))
        {
            var explanation = explanationProp.GetString();
            if (!string.IsNullOrWhiteSpace(explanation))
            {
                Output.PrintMarkup($"  [{Style.General.Muted}] {MarkupEscape(explanation)}[/]");
                Output.PrintLine("", Style.General.Muted);
            }
        }

        // Plan steps
        if (!args.TryGetProperty("plan", out var planProp) || planProp.ValueKind != JsonValueKind.Array)
        {
            Output.PrintLine("  (no plan steps)", Style.General.Muted);
            return;
        }

        var stepIndex = 1;
        foreach (var step in planProp.EnumerateArray())
        {
            if (!step.TryGetProperty("step", out var stepTextProp))
                continue;

            var stepText = stepTextProp.GetString() ?? "";
            var status = step.TryGetProperty("status", out var statusProp)
                ? statusProp.GetString() ?? "pending"
                : "pending";

            RenderStep(stepIndex, stepText, status);
            stepIndex++;
        }
    }

    private void RenderStep(int index, string stepText, string status)
    {
        var (icon, label, color) = GetStatusDisplay(status);
        var statusTag = FormatStatusTag(icon, label, color);

        Output.PrintLine("", Style.General.Muted);

        // Step number + status tag
        Output.PrintMarkup($"  [{Style.General.Muted}]#{index,2}[/] ");
        Output.PrintMarkup(statusTag);

        // Step description
        Output.PrintMarkup($"  [{Style.General.Value}]{MarkupEscape(stepText)}[/]");
    }

    private static (string icon, string label, string color) GetStatusDisplay(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "completed"    => ("\u2705", "Completed",   Style.Messages.Success),
            "in_progress"  => ("\u27a1\ufe0f", "In Progress", Style.Messages.Warning),
            "pending"      => ("\u2b55", "Pending",     Style.General.Muted),
            "skipped"      => ("\u25c7", "Skipped",     Style.General.Muted),
            _              => ("\u00b7", status,        Style.General.Muted),
        };
    }

    private static string FormatStatusTag(string icon, string label, string color)
    {
        var paddedLabel = label.PadRight(StatusWidth);
        return $"[{Style.Messages.Emphasis} {color}]{icon} {paddedLabel}[/]";
    }

    private static string MarkupEscape(string text)
    {
        return text.Replace("[", "[[").Replace("]", "]]");
    }
}

using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using StreamShell;

namespace OpenClawPTT.ConfigWizard;

/// <summary>Configures visual feedback indicator settings.</summary>
public sealed class VisualFeedbackConfigSection : IConfigSectionWizard
{
    public string Name => "Visual Feedback";
    public string Description => "Recording indicator appearance and position";

    private static readonly Regex HexColorPattern = new(@"^#?([0-9A-Fa-f]{6})$", RegexOptions.Compiled);

    public async Task<ConfigSectionResult> RunAsync(IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct)
    {
        var result = new ConfigSectionResult();
        bool changed = false;

        // ── Enabled ──
        var enabled = await PromptSelectionHelper.PromptBoolAsync(host,
            "Show visual feedback indicator?",
            config.VisualFeedbackEnabled, allowCancel: false, cancellationToken: ct);

        if (enabled.HasValue && enabled.Value != config.VisualFeedbackEnabled)
        {
            config.VisualFeedbackEnabled = enabled.Value;
            changed = true;
        }

        if (!enabled.HasValue || !enabled.Value)
        {
            result.IsChanged = changed;
            return result;
        }

        // ── Visual mode ──
        var visualMode = await PromptSelectionHelper.PromptEnumAsync<VisualMode>(host,
            "Visual indicator style:", config.VisualMode, allowCancel: false, cancellationToken: ct);
        if (visualMode.HasValue && visualMode.Value != config.VisualMode)
        {
            config.VisualMode = visualMode.Value;
            changed = true;
        }

        // ── Position ──
        var positions = new (string Name, string Value)[]
        {
            ("Top Left", "TopLeft"),
            ("Top Right", "TopRight"),
            ("Bottom Left", "BottomLeft"),
            ("Bottom Right", "BottomRight"),
        };
        string position;
        if (isInitialSetup)
        {
            position = await PromptSelectionHelper.PromptStringAsync(host,
                "Indicator position:", positions, config.VisualFeedbackPosition, allowCancel: false, ct);
        }
        else
        {
            var posResult = await PromptSelectionHelper.PromptStringWithBackAsync(host,
                "Indicator position:", positions, config.VisualFeedbackPosition, ct);
            if (posResult == null)
            {
                result.IsChanged = changed;
                return result;
            }
            position = posResult;
        }
        if (position != config.VisualFeedbackPosition)
        {
            config.VisualFeedbackPosition = position;
            changed = true;
        }

        // ── Size ──
        var size = await PromptTextHelper.PromptIntAsync(host, "Indicator size (1–200 pixels)",
            config.VisualFeedbackSize, 1, 200, ct);
        if (size.HasValue && size.Value != config.VisualFeedbackSize)
        {
            config.VisualFeedbackSize = size.Value;
            changed = true;
        }

        // ── Opacity ──
        var opacity = await PromptTextHelper.PromptDoubleAsync(host, "Indicator opacity (0.0–1.0)",
            config.VisualFeedbackOpacity, 0.0, 1.0, ct);
        if (opacity.HasValue && Math.Abs(opacity.Value - config.VisualFeedbackOpacity) > 0.001)
        {
            config.VisualFeedbackOpacity = opacity.Value;
            changed = true;
        }

        // ── Color ──
        var color = await PromptTextHelper.PromptAsync(host, "Indicator color (hex, e.g. #FF0000)",
            config.VisualFeedbackColor,
            v => HexColorPattern.IsMatch(v), "Expected hex color like #00FF00",
            ct);
        if (color != null && color != config.VisualFeedbackColor)
        {
            config.VisualFeedbackColor = color.StartsWith("#") ? color : $"#{color}";
            changed = true;
        }

        // ── Rim thickness ──
        var rim = await PromptTextHelper.PromptIntAsync(host, "Rim thickness (0 = off, 1–50)",
            config.VisualFeedbackRimThickness, 0, 50, ct);
        if (rim.HasValue && rim.Value != config.VisualFeedbackRimThickness)
        {
            config.VisualFeedbackRimThickness = rim.Value;
            changed = true;
        }

        result.IsChanged = changed;
        return result;
    }
}

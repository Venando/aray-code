using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ArayCode.Services;
using StreamShell;

namespace ArayCode.ConfigWizard;

/// <summary>Configures visual feedback indicator settings.</summary>
public sealed class VisualFeedbackConfigSection : ConfigSectionBase
{
    public override string Name => "Visual Feedback";
    public override string Description => "Recording indicator appearance and position";

    private static readonly Regex HexColorPattern = new(@"^#?([0-9A-Fa-f]{6})$", RegexOptions.Compiled);

    private static readonly (string Name, string Value)[] PositionOptions =
    {
        ("Top Left", "TopLeft"),
        ("Top Right", "TopRight"),
        ("Bottom Left", "BottomLeft"),
        ("Bottom Right", "BottomRight"),
    };

    // Position and Color run inline before the rest (they need custom ordering).
    private const int IndexPosition = 0;
    private const int IndexColor = 1;
    private const int IndexRest = 2;

    public VisualFeedbackConfigSection()
    {
        _configItems.Add(ConfigSetupItem.ForSelectionWithBack(
            title: "Indicator position",
            fieldName: nameof(AppConfig.VisualFeedbackPosition),
            options: PositionOptions));

        _configItems.Add(ConfigSetupItem.ForString(
            title: "Indicator color (hex, e.g. #FF0000)",
            fieldName: nameof(AppConfig.VisualFeedbackColor),
            validator: v => HexColorPattern.IsMatch(v),
            validationHint: "Expected hex color like #00FF00",
            transform: v => v.StartsWith("#") ? v : $"#{v}"));

        _configItems.AddRange(new[]
        {
            ConfigSetupItem.ForEnum<VisualMode>(
                title: "Visual indicator style",
                fieldName: nameof(AppConfig.VisualMode)),

            ConfigSetupItem.ForInt(
                title: "Indicator size (1–200 pixels)",
                fieldName: nameof(AppConfig.VisualFeedbackSize),
                min: 1,
                max: 200),

            ConfigSetupItem.ForDouble(
                title: "Indicator opacity (0.0–1.0)",
                fieldName: nameof(AppConfig.VisualFeedbackOpacity),
                min: 0.0,
                max: 1.0),

            ConfigSetupItem.ForInt(
                title: "Rim thickness (0 = off, 1–50)",
                fieldName: nameof(AppConfig.VisualFeedbackRimThickness),
                min: 0,
                max: 50),
        });
    }

    public override async Task<ConfigSectionResult> RunAsync(
        IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct)
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
        result.Settings.Add(new ConfigSectionResult.SettingRecord(
            "Visual feedback", config.VisualFeedbackEnabled ? "enabled" : "disabled"));

        if (!enabled.HasValue || !enabled.Value)
        {
            result.IsChanged = changed;
            return result;
        }

        // ── Position (index 0) and Color (index 1) — run inline, before auto-config items ──
        for (int i = IndexPosition; i < IndexRest; i++)
        {
            if (await _configItems[i].RunAsync(host, config, isInitialSetup, ct))
                changed = true;
            result.Settings.Add(new ConfigSectionResult.SettingRecord(
                _configItems[i].Title, _configItems[i].GetDisplayValue(config)));
        }

        // ── VisualMode, Size, Opacity, RimThickness (index 2..) ──
        if (await RunConfigItemsAsync(host, config, isInitialSetup, ct, result, startIndex: IndexRest))
            changed = true;

        result.IsChanged = changed;
        return result;
    }
}

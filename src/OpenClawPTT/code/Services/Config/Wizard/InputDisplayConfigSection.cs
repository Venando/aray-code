using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using StreamShell;

namespace OpenClawPTT.ConfigWizard;

/// <summary>Configures input, display, and audio response settings.</summary>
public sealed class InputDisplayConfigSection : ConfigSectionBase
{
    public override string Name => "Input & Display";
    public override string Description => "Hotkey, display mode, and audio response settings";

    private static readonly (string Name, string Value)[] AudioModeOptions =
    {
        ("Text only", "text-only"),
        ("Audio only", "audio-only"),
        ("Both text and audio", "both"),
    };

    public InputDisplayConfigSection()
    {
        _configItems.AddRange(new[]
        {
            ConfigSetupItem.ForString(
                title: "PTT hotkey (e.g. Alt+= or Ctrl+Shift+Space)",
                fieldName: nameof(AppConfig.HotkeyCombination),
                validator: v =>
                {
                    try { HotkeyMapping.Parse(v); return true; }
                    catch { return false; }
                },
                validationHint: "Expected format like Alt+= or Ctrl+Shift+Space"),

            ConfigSetupItem.ForBool(
                title: "Hold-to-talk mode? (Hold = hold down, Release = send)",
                fieldName: nameof(AppConfig.HoldToTalk)),

            ConfigSetupItem.ForBool(
                title: "Show real-time reply streaming?",
                fieldName: nameof(AppConfig.RealTimeReplyOutput)),

            ConfigSetupItem.ForEnum<ReplyDisplayMode>(
                title: "Reply display mode",
                fieldName: nameof(AppConfig.ReplyDisplayMode)),

            ConfigSetupItem.ForString(
                title: "Transcription context prefix",
                fieldName: nameof(AppConfig.TranscriptionPromptPrefix),
                isEmptyToDefault: true,
                allowClear: true),

            ConfigSetupItem.ForBool(
                title: "Require confirmation before sending messages?",
                fieldName: nameof(AppConfig.RequireConfirmBeforeSend)),
        });

        // Audio response mode (with Back button for reconfig)
        _configItems.Add(ConfigSetupItem.ForSelectionWithBack(
            title: "Audio response mode",
            fieldName: nameof(AppConfig.AudioResponseMode),
            options: AudioModeOptions));
    }

    public override async Task<ConfigSectionResult> RunAsync(
        IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct)
    {
        var result = new ConfigSectionResult();
        bool changed = false;

        // ── Universal config items ──
        if (await RunConfigItemsAsync(host, config, isInitialSetup, ct, result))
            changed = true;

        result.IsChanged = changed;
        return result;
    }
}

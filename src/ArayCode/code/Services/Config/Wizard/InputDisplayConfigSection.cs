using System;
using System.Threading;
using System.Threading.Tasks;
using ArayCode.Services;
using StreamShell;

namespace ArayCode.ConfigWizard;

/// <summary>Configures input, display, and audio response settings.</summary>
public sealed class InputDisplayConfigSection : ConfigSectionBase
{
    public override string Name => "Input & Display";
    public override string Description => "Hotkey, display mode, and audio response settings";

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
                validationHint: "Expected format like Alt+= or Ctrl+Shift+Space",
                isEmptyToDefault: true),

            ConfigSetupItem.ForBool(
                title: "Hold-to-talk mode? (Hold = hold down, Release = send)",
                fieldName: nameof(AppConfig.HoldToTalk)),

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

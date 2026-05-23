using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using OpenClawPTT.TTS;
using OpenClawPTT.TTS.Providers;
using OpenClawPTT.Services.Themes;
using StreamShell;

namespace OpenClawPTT.ConfigWizard;

/// <summary>Configures Text-To-Speech settings.</summary>
public sealed class TtsConfigSection : ConfigSectionBase
{
    public override string Name => "Text-To-Speech";
    public override string Description => "TTS provider and voice settings";

    // ── Option tables (static, moved to top for clarity) ────────────

    private static readonly (string Name, string Value)[] TtsProviderOptions =
    [
        ("OpenAI", "OpenAI"),
        ("Edge", "Edge"),
        ("Coqui TTS (uv)", "CoquiUv"),
        ("Supertonic 3", "Supertonic"),
        ("Piper", "Piper"),
    ];

    private static readonly (string Name, string Value)[] TtsModeOptions =
    [
        ("Always on", "always-on"),
        ("SISO (Sound-in-Sound-out)", "siso"),
        ("Off", "off"),
    ];

    // ── Item indices ────────────────────────────────────────────────

    private const int IndexProvider = 0;
    private const int IndexVoice = 1;
    private const int IndexTtsMode = 2;

    public TtsConfigSection()
    {
        // Order matters: provider (index 0) runs first so tagged items come next
        _configItems.AddRange(
        [
            ConfigSetupItem.ForSelectionWithBack(
                title: "Choose TTS provider",
                fieldName: nameof(AppConfig.TtsProvider),
                options: TtsProviderOptions),

            ConfigSetupItem.ForString(
                title: "Voice name (optional)",
                fieldName: nameof(AppConfig.TtsVoice),
                isEmptyToDefault: true),

            ConfigSetupItem.ForSelection(
                title: "TTS output mode",
                fieldName: nameof(AppConfig.TtsOutputMode),
                options: TtsModeOptions),
        ]);

        AddOpenAiItems();
        AddEdgeItems();
        AddPiperItems();
        AddSupertonicItems();

        // Note: "Coqui" and "Python" tagged items are intentionally omitted.
        // The old Coqui/Python provider-specific config fields (CoquiModelPath,
        // PythonPath, CoquiModelName) are now managed through the unified CoquiUv
        // provider flow instead of the tagged-item system.
    }

    private void AddOpenAiItems()
    {
        AddConfigItem("OpenAI", ConfigSetupItem.ForString(
            title: "OpenAI API key for TTS",
            fieldName: nameof(AppConfig.TtsOpenAiApiKey),
            isSecret: true));
    }

    private void AddEdgeItems()
    {
        AddConfigItem("Edge", ConfigSetupItem.ForString(
            title: "Azure TTS subscription key",
            fieldName: nameof(AppConfig.TtsSubscriptionKey),
            isSecret: true,
            isEmptyToDefault: true));

        AddConfigItem("Edge", ConfigSetupItem.ForString(
            title: "Azure TTS region",
            fieldName: nameof(AppConfig.TtsRegion)));
    }

    private void AddPiperItems()
    {
        AddConfigItem("Piper", ConfigSetupItem.ForString(
            title: "Piper binary path",
            fieldName: nameof(AppConfig.PiperPath)));

        AddConfigItem("Piper", ConfigSetupItem.ForString(
            title: "Piper model path",
            fieldName: nameof(AppConfig.PiperModelPath),
            isEmptyToDefault: true));
    }

    private void AddSupertonicItems()
    {
        AddConfigItem("Supertonic", ConfigSetupItem.ForSelection(
            title: "Voice",
            fieldName: nameof(AppConfig.TtsSupertonicVoice),
            options:
            [
                ("M1 (Male 1)", "M1"),
                ("M2 (Male 2)", "M2"),
                ("M3 (Male 3)", "M3"),
                ("M4 (Male 4)", "M4"),
                ("M5 (Male 5)", "M5"),
                ("F1 (Female 1)", "F1"),
                ("F2 (Female 2)", "F2"),
                ("F3 (Female 3)", "F3"),
                ("F4 (Female 4)", "F4"),
                ("F5 (Female 5)", "F5"),
            ]));

        AddConfigItem("Supertonic", ConfigSetupItem.ForSelection(
            title: "Language",
            fieldName: nameof(AppConfig.TtsSupertonicLang),
            options:
            [
                ("English (en)", "en"),
                ("Japanese (ja)", "ja"),
                ("Korean (ko)", "ko"),
                ("Ukrainian (uk)", "uk"),
                ("Russian (ru)", "ru"),
                ("Arabic (ar)", "ar"),
                ("Auto-detect (na)", "na"),
            ]));

        AddConfigItem("Supertonic", ConfigSetupItem.ForString(
            title: "Quality (5=low, 12=high, default 8)",
            fieldName: nameof(AppConfig.TtsSupertonicQuality),
            isEmptyToDefault: true));

        AddConfigItem("Supertonic", ConfigSetupItem.ForString(
            title: "Speed (0.7=slow, 2.0=fast, default 1.05)",
            fieldName: nameof(AppConfig.TtsSupertonicSpeed),
            isEmptyToDefault: true));
    }

    // ── Section runner ──────────────────────────────────────────────

    public override async Task<ConfigSectionResult> RunAsync(
        IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct)
    {
        var result = new ConfigSectionResult();
        bool changed = false;

        var setupTts = await PromptSelectionHelper.PromptSkipOrProceedAsync(host,
            "Setup Text-To-Speech?", allowCancel: true, cancellationToken: ct);
        if (!setupTts.HasValue || !setupTts.Value)
        {
            host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Info}]  Skipped TTS setup.[/]");
            result.IsChanged = false;
            return result;
        }

        ConfigSelectionHelper.PrintSubSection(host, "proceeding");

        // ── Provider selection (index 0, must run before tagged items) ──
        if (await _configItems[IndexProvider].RunAsync(host, config, isInitialSetup, ct))
            changed = true;
        result.Settings.Add(new ConfigSectionResult.SettingRecord(
            _configItems[IndexProvider].Title, _configItems[IndexProvider].GetDisplayValue(config)));

        ConfigSelectionHelper.PrintSubSection(host, config.TtsProvider.ToString());

        // ── Coqui TTS (uv): delegate to model download/choose flow ──
        if (config.TtsProvider == TtsProviderType.CoquiUv)
        {
            if (await RunCoquiFlowAsync(host, config, ct))
                changed = true;

            result.Settings.Add(new ConfigSectionResult.SettingRecord(
                "Coqui TTS Model", config.CoquiModelName ?? "none"));
        }

        // ── Seed provider-specific defaults ──
        SeedDefaults(config);

        // ── Run provider-specific config items by tag ──
        if (config.TtsProvider != TtsProviderType.CoquiUv)
        {
            string[] providerTags = [config.TtsProvider.ToString()];
            foreach (var tag in providerTags)
            {
                if (await RunConfigItemsByTagAsync(tag, host, config, isInitialSetup, ct, result))
                    changed = true;
            }
        }

        // ── Voice and TTS output mode (indices 1..) ──
        // For CoquiUv and Supertonic, skip "Voice name" — they have their own voice config items
        var skipVoice = config.TtsProvider is TtsProviderType.CoquiUv or TtsProviderType.Supertonic;
        var startIndex = skipVoice ? IndexTtsMode : IndexVoice;
        if (await RunConfigItemsAsync(host, config, isInitialSetup, ct, result, startIndex: startIndex))
            changed = true;

        result.IsChanged = changed;
        return result;
    }

    /// <summary>
    /// Runs the Coqui TTS model selection/download flow.
    /// Extracted from <see cref="RunAsync"/> (SRP).
    /// </summary>
    private static async Task<bool> RunCoquiFlowAsync(
        IStreamShellHost host, AppConfig config, CancellationToken ct)
    {
        var coquiFlow = new CoquiTtsConfigFlow();
        return await coquiFlow.RunAsync(host, config, ct);
    }

    /// <summary>
    /// Seeds sensible defaults for provider-specific configuration values.
    /// Extracted from <see cref="RunAsync"/> (SRP).
    /// </summary>
    private static void SeedDefaults(AppConfig config)
    {
        config.TtsRegion ??= "eastus";
        config.CoquiModelName ??= "tts_models/multilingual/mxtts/vits";
        config.PiperPath ??= "piper";
        config.TtsSupertonicVoice ??= "M1";
        config.TtsSupertonicLang ??= "en";
        config.TtsSupertonicQuality ??= "8";
        config.TtsSupertonicSpeed ??= "1.05";
    }
}

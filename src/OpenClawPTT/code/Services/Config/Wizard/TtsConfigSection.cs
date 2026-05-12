using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using OpenClawPTT.TTS;
using StreamShell;

namespace OpenClawPTT.ConfigWizard;

/// <summary>Configures Text-To-Speech settings.</summary>
public sealed class TtsConfigSection : ConfigSectionBase
{
    public override string Name => "Text-To-Speech";
    public override string Description => "TTS provider and voice settings";

    private static readonly (string Name, string Value)[] TtsProviderOptions =
    {
        ("OpenAI", "OpenAI"),
        ("Edge", "Edge"),
        ("Coqui", "Coqui"),
        ("Piper", "Piper"),
        ("Python", "Python"),
        ("ElevenLabs (not supported)", "ElevenLabs"),
    };

    private static readonly (string Name, string Value)[] TtsModeOptions =
    {
        ("Always on", "always-on"),
        ("SISO (Sound-in-Sound-out)", "siso"),
        ("Off", "off"),
    };

    private const int IndexProvider = 0;
    private const int IndexVoice = 1;
    private const int IndexTtsMode = 2;

    public TtsConfigSection()
    {
        // Order matters: provider (index 0) runs first so ElevenLabs check + tagged items come next
        _configItems.AddRange(new[]
        {
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
        });

        // ── OpenAI items ──
        AddConfigItem("OpenAI", ConfigSetupItem.ForString(
            title: "OpenAI API key for TTS",
            fieldName: nameof(AppConfig.TtsOpenAiApiKey),
            isSecret: true));

        // ── Edge items ──
        AddConfigItem("Edge", ConfigSetupItem.ForString(
            title: "Azure TTS subscription key",
            fieldName: nameof(AppConfig.TtsSubscriptionKey),
            isSecret: true,
            isEmptyToDefault: true));

        AddConfigItem("Edge", ConfigSetupItem.ForString(
            title: "Azure TTS region",
            fieldName: nameof(AppConfig.TtsRegion)));

        // ── Coqui items (also falls through to Python) ──
        AddConfigItem("Coqui", ConfigSetupItem.ForString(
            title: "Path to Coqui model file",
            fieldName: nameof(AppConfig.CoquiModelPath),
            isEmptyToDefault: true));

        // ── Python items ──
        AddConfigItem("Python", ConfigSetupItem.ForString(
            title: "Python path",
            fieldName: nameof(AppConfig.PythonPath),
            isEmptyToDefault: true));

        AddConfigItem("Python", ConfigSetupItem.ForString(
            title: "Coqui model name",
            fieldName: nameof(AppConfig.CoquiModelName)));

        // ── Piper items ──
        AddConfigItem("Piper", ConfigSetupItem.ForString(
            title: "Piper binary path",
            fieldName: nameof(AppConfig.PiperPath)));

        AddConfigItem("Piper", ConfigSetupItem.ForString(
            title: "Piper model path",
            fieldName: nameof(AppConfig.PiperModelPath),
            isEmptyToDefault: true));
    }

    public override async Task<ConfigSectionResult> RunAsync(
        IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct)
    {
        var result = new ConfigSectionResult();
        bool changed = false;

        var setupTts = await PromptSelectionHelper.PromptSkipOrProceedAsync(host,
            "Setup Text-To-Speech?", allowCancel: true, cancellationToken: ct);
        if (!setupTts.HasValue || !setupTts.Value)
        {
            host.AddMessage("[grey]  Skipped TTS setup.[/]");
            result.IsChanged = false;
            return result;
        }

        ConfigSelectionHelper.PrintSubSection(host, "proceeding");

        // ── Provider selection (index 0, must run before ElevenLabs check) ──
        if (await _configItems[IndexProvider].RunAsync(host, config, isInitialSetup, ct))
            changed = true;
        result.Settings.Add(new ConfigSectionResult.SettingRecord(
            _configItems[IndexProvider].Title, _configItems[IndexProvider].GetDisplayValue(config)));

        ConfigSelectionHelper.PrintSubSection(host, config.TtsProvider.ToString());

        // ── ElevenLabs is not supported yet ──
        if (config.TtsProvider == TtsProviderType.ElevenLabs)
        {
            host.AddMessage("[yellow]  ElevenLabs TTS is not yet supported.[/]");
            result.IsChanged = changed;
            return result;
        }

        // ── Seed provider-specific defaults ──
        config.TtsRegion ??= "eastus";
        config.PythonPath ??= "python";
        config.CoquiModelName ??= "tts_models/multilingual/mxtts/vits";
        config.PiperPath ??= "piper";

        // ── Run provider-specific items by tag ──
        string[] providerTags = config.TtsProvider switch
        {
            TtsProviderType.Coqui => new[] { "Coqui", "Python" },
            TtsProviderType.ElevenLabs => Array.Empty<string>(),
            _ => new[] { config.TtsProvider.ToString() },
        };

        foreach (var tag in providerTags)
        {
            if (await RunConfigItemsByTagAsync(tag, host, config, isInitialSetup, ct, result))
                changed = true;
        }

        // ── Voice and TTS output mode (indices 1..) ──
        if (await RunConfigItemsAsync(host, config, isInitialSetup, ct, result, startIndex: IndexVoice))
            changed = true;

        result.IsChanged = changed;
        return result;
    }
}

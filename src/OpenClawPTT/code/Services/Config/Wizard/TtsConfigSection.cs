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
        ("SISO (single-in-single-out)", "siso"),
        ("Off", "off"),
    };

    public TtsConfigSection()
    {
        // Universal items (always prompted)
        _configItems.AddRange(new[]
        {
            ConfigSetupItem.ForString(
                title: "Voice name (optional)",
                fieldName: nameof(AppConfig.TtsVoice),
                isEmptyToDefault: true),
        });

        // ── OpenAI items ──
        AddConfigItem("OpenAI", ConfigSetupItem.ForString(
            title: "OpenAI API key for TTS",
            fieldName: nameof(AppConfig.TtsOpenAiApiKey),
            isSecret: true,
            isEmptyToDefault: true));

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
            fieldName: nameof(AppConfig.PythonPath)));

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

        // ── Provider selection ──

        string providerStr = await PromptSelectionHelper.PromptStringWithBackAsync(host,
            "Choose TTS provider:", TtsProviderOptions, config.TtsProvider.ToString(), ct);
        if (providerStr == null)
        {
            result.IsChanged = changed;
            return result;
        }

        if (Enum.TryParse<TtsProviderType>(providerStr, out var provider) && provider != config.TtsProvider)
        {
            config.TtsProvider = provider;
            changed = true;
        }

        ConfigSelectionHelper.PrintSubSection(host, providerStr);

        if (providerStr == "ElevenLabs")
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
        string[] providerTags = provider switch
        {
            TtsProviderType.Coqui => new[] { "Coqui", "Python" },  // Coqui falls through to Python
            _ => new[] { providerStr },
        };

        foreach (var tag in providerTags)
        {
            if (await RunConfigItemsByTagAsync(tag, host, config, isInitialSetup, ct, result))
                changed = true;
        }

        // ── TTS Output Mode (always prompted) ──
        var ttsMode = await PromptSelectionHelper.PromptStringAsync(host,
            "TTS output mode:", TtsModeOptions, config.TtsOutputMode, allowCancel: false, cancellationToken: ct);
        if (ttsMode != config.TtsOutputMode)
        {
            config.TtsOutputMode = ttsMode;
            changed = true;
        }

        // ── Universal config items (voice) ──
        if (await RunConfigItemsAsync(host, config, isInitialSetup, ct, result))
            changed = true;

        result.IsChanged = changed;
        return result;
    }
}

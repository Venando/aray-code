using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;

namespace OpenClawPTT.ConfigWizard;

/// <summary>Configures Speech-To-Text settings.</summary>
public sealed class SttConfigSection : ConfigSectionBase
{
    public override string Name => "Speech-To-Text";
    public override string Description => "STT provider and transcription settings";

    // Provider tag constants
    private const string TagGroq = "groq";
    private const string TagOpenAi = "openai";
    private const string TagWhisperCpp = "whisper-cpp";

    private static readonly (string Name, string Value)[] ProviderOptions =
    {
        ("Groq", TagGroq),
        ("OpenAI", TagOpenAi),
        ("Whisper.cpp (local)", TagWhisperCpp),
    };

    public SttConfigSection()
    {
        // Universal items (always prompted)
        // NOTE: locale is only needed for cloud STT providers (Groq, OpenAI).
        // Whisper.cpp auto-detects language from audio.

        // ── Groq items ──
        AddConfigItem(TagGroq, ConfigSetupItem.ForString(
            title: "Groq API key (starts with gsk_)",
            fieldName: nameof(AppConfig.GroqApiKey),
            validator: v => v.StartsWith("gsk_"),
            validationHint: "Must start with gsk_",
            isSecret: true));

        AddConfigItem(TagGroq, ConfigSetupItem.ForString(
            title: "Groq STT model",
            fieldName: nameof(AppConfig.GroqModel),
            isEmptyToDefault:true));

        AddConfigItem(TagGroq, ConfigSetupItem.ForString(
            title: "Locale (e.g. en-US, ja-JP, ru-RU)",
            fieldName: nameof(AppConfig.Locale),
            validator: v => v.Length >= 2,
            validationHint: "At least 2 characters",
            isEmptyToDefault:true));

        // ── OpenAI items ──
        AddConfigItem(TagOpenAi, ConfigSetupItem.ForString(
            title: "OpenAI API key for STT",
            fieldName: nameof(AppConfig.OpenAiApiKey),
            isSecret: true,
            isEmptyToDefault: true));

        AddConfigItem(TagOpenAi, ConfigSetupItem.ForString(
            title: "OpenAI STT model",
            fieldName: nameof(AppConfig.OpenAiModel)));

        AddConfigItem(TagOpenAi, ConfigSetupItem.ForString(
            title: "Locale (e.g. en-US, ja-JP, ru-RU)",
            fieldName: nameof(AppConfig.Locale),
            validator: v => v.Length >= 2,
            validationHint: "At least 2 characters",
            isEmptyToDefault:true));
    }

    public override async Task<ConfigSectionResult> RunAsync(
        IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct)
    {
        var result = new ConfigSectionResult();
        bool changed = false;

        // ── On initial setup: ask Yes/Skip ──
        if (isInitialSetup)
        {
            var setupStt = await PromptSelectionHelper.PromptSkipOrProceedAsync(host,
                "Setup Speech-To-Text?", allowCancel: true, cancellationToken: ct);
            if (!setupStt.HasValue || !setupStt.Value)
            {
                host.AddMessage("[grey]  Skipped STT setup.[/]");
                result.IsChanged = false;
                return result;
            }
        }

        ConfigSelectionHelper.PrintSubSection(host, "proceeding");

        // ── Provider selection ──
        string? provider = await PromptSelectionHelper.PromptStringAsync(host,
            "Choose STT provider:", ProviderOptions, cancellationToken: ct);

        if (provider == null)
        {
            result.IsChanged = false;
            return result;
        }

        ConfigSelectionHelper.PrintSubSection(host, provider, "");

        if (provider != config.SttProvider)
        {
            config.SttProvider = provider;
            changed = true;
        }
        else if (!isInitialSetup)
        {
            // During reconfigure, user explicitly chose same provider — mark changed
            // to ensure config is saved (items may have been reviewed/re-entered)
            changed = true;
        }

        // ── Seed provider-specific defaults ──
        config.GroqModel ??= "whisper-large-v3";
        config.OpenAiModel ??= "whisper-1";

        // ── Whisper.cpp: delegate to WhisperConfigFlow ──
        if (provider == TagWhisperCpp)
        {
            var whisperFlow = new WhisperConfigFlow();
            if (await whisperFlow.RunAsync(host, config, ct))
                changed = true;

            result.Settings.Add(new ConfigSectionResult.SettingRecord(
                "Whisper Model", config.WhisperCppModel ?? "none"));
        }
        else
        {
            // ── Run provider-specific items by tag for Groq/OpenAI ──
            if (await RunConfigItemsByTagAsync(provider, host, config, isInitialSetup, ct, result))
                changed = true;
        }

        // ── Generic config items (Locale) ──
        if (await RunConfigItemsAsync(host, config, isInitialSetup, ct, result))
            changed = true;

        result.IsChanged = changed;
        return result;
    }
}

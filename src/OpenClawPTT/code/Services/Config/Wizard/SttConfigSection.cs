using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using StreamShell;

namespace OpenClawPTT.ConfigWizard;

/// <summary>Configures Speech-To-Text settings.</summary>
public sealed class SttConfigSection : ConfigSectionBase
{
    public override string Name => "Speech-To-Text";
    public override string Description => "STT provider and transcription settings";

    // Provider tag constants — also used in the selection prompt as the returned Value
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
        _configItems.AddRange(new[]
        {
            ConfigSetupItem.ForString(
                title: "Locale (e.g. en-US, ja-JP, ru-RU)",
                fieldName: nameof(AppConfig.Locale),
                validator: v => v.Length >= 2,
                validationHint: "At least 2 characters",
                isEmptyToDefault:true),
        });

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

        // ── OpenAI items ──
        AddConfigItem(TagOpenAi, ConfigSetupItem.ForString(
            title: "OpenAI API key for STT",
            fieldName: nameof(AppConfig.OpenAiApiKey),
            isSecret: true,
            isEmptyToDefault: true));

        AddConfigItem(TagOpenAi, ConfigSetupItem.ForString(
            title: "OpenAI STT model",
            fieldName: nameof(AppConfig.OpenAiModel)));

        // ── Whisper.cpp items ──
        AddConfigItem(TagWhisperCpp, ConfigSetupItem.ForString(
            title: "Path to whisper-cpp executable",
            fieldName: nameof(AppConfig.WhisperCppPath),
            isEmptyToDefault: true));

        AddConfigItem(TagWhisperCpp, ConfigSetupItem.ForString(
            title: "Path to whisper-cpp model file",
            fieldName: nameof(AppConfig.WhisperCppModelPath),
            isEmptyToDefault: true));
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

        // ── Seed provider-specific defaults ──
        config.GroqModel ??= "whisper-large-v3";
        config.OpenAiModel ??= "whisper-1";

        // ── Run provider-specific items by tag ──
        if (await RunConfigItemsByTagAsync(provider, host, config, isInitialSetup, ct, result))
            changed = true;

        // ── Generic config items (Locale, etc.) ──
        if (await RunConfigItemsAsync(host, config, isInitialSetup, ct, result))
            changed = true;

        result.IsChanged = changed;
        return result;
    }
}

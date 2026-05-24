using System.Threading;
using System.Threading.Tasks;
using ArayCode.Services;
using ArayCode.Services.Themes;
using StreamShell;

namespace ArayCode.ConfigWizard;

/// <summary>Configures direct LLM access settings (bypass agent for direct LLM calls).</summary>
public sealed class DirectLlmConfigSection : ConfigSectionBase
{
    public override string Name => "Direct LLM";
    public override string Description => "Direct LLM access settings (URL, model, API type)";

    private static readonly (string Name, string Value)[] ApiTypeOptions =
    {
        ("OpenAI-compatible", "openai-completions"),
        ("Anthropic Messages", "anthropic-messages"),
    };

    public DirectLlmConfigSection()
    {
        _configItems.AddRange(new[]
        {
            ConfigSetupItem.ForString(
                title: "Direct LLM URL",
                fieldName: nameof(AppConfig.DirectLlmUrl),
                validator: v => string.IsNullOrWhiteSpace(v) || Uri.TryCreate(v, UriKind.Absolute, out _),
                validationHint: "Expected absolute URL (e.g. http://localhost:11434/v1)",
                isEmptyToDefault: true),

            ConfigSetupItem.ForString(
                title: "Direct LLM API token (optional)",
                fieldName: nameof(AppConfig.DirectLlmToken),
                isSecret: true,
                isEmptyToDefault: true),

            ConfigSetupItem.ForString(
                title: "Direct LLM model name",
                fieldName: nameof(AppConfig.DirectLlmModelName),
                isEmptyToDefault: true),

            ConfigSetupItem.ForSelection(
                title: "Direct LLM API type",
                fieldName: nameof(AppConfig.DirectLlmApiType),
                options: ApiTypeOptions,
                description: "API format used by the LLM provider"),
        });
    }

    public override async Task<ConfigSectionResult> RunAsync(
        IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct)
    {
        var result = new ConfigSectionResult();
        bool changed = false;

        // ── On initial setup: ask Yes/Skip ──
        if (isInitialSetup)
        {
            var setupDirectLlm = await PromptSelectionHelper.PromptSkipOrProceedAsync(host,
                "Setup Direct LLM? (for /llm command and TTS summarization)", allowCancel: true, cancellationToken: ct);
            if (!setupDirectLlm.HasValue || !setupDirectLlm.Value)
            {
                host.AddMessage($"[{ThemeProvider.Current.Tools.General.Muted}]  Skipped Direct LLM setup — Direct LLM disabled.[/]");
                config.DirectLlmUrl = null;
                config.DirectLlmModelName = null;
                result.IsChanged = true;
                return result;
            }
        }

        ConfigSelectionHelper.PrintSubSection(host, "proceeding");

        // ── Run all config items ──
        if (await RunConfigItemsAsync(host, config, isInitialSetup, ct, result))
            changed = true;

        result.IsChanged = changed;
        return result;
    }
}

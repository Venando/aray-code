using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using OpenClawPTT.TTS;
using StreamShell;

namespace OpenClawPTT.ConfigWizard;

/// <summary>Configures Text-To-Speech settings.</summary>
public sealed class TtsConfigSection : IConfigSectionWizard
{
    public string Name => "Text-To-Speech";
    public string Description => "TTS provider and voice settings";

    public async Task<ConfigSectionResult> RunAsync(IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct)
    {
        var result = new ConfigSectionResult();
        bool changed = false;

        // ── On initial setup: ask Yes/Skip ──
        if (isInitialSetup)
        {
            var setupTts = await PromptSelectionHelper.PromptBoolAsync(host,
                "Setup Text-To-Speech?", defaultValue: true, allowCancel: false, cancellationToken: ct);
            if (!setupTts.HasValue || !setupTts.Value)
            {
                host.AddMessage("[grey]  Skipped TTS setup.[/]");
                result.IsChanged = false;
                return result;
            }
        }

        // ── Provider selection ──
        var providers = new (string Name, string Value)[]
        {
            ("OpenAI", "OpenAI"),
            ("Edge", "Edge"),
            ("Coqui", "Coqui"),
            ("Piper", "Piper"),
            ("Python", "Python"),
            ("ElevenLabs (not supported)", "ElevenLabs"),
        };

        string providerStr;
        if (isInitialSetup)
        {
            providerStr = await PromptSelectionHelper.PromptStringAsync(host,
                "Choose TTS provider:", providers, config.TtsProvider.ToString(), allowCancel: false, cancellationToken: ct);
        }
        else
        {
            var providerResult = await PromptSelectionHelper.PromptStringWithBackAsync(host,
                "Choose TTS provider:", providers, config.TtsProvider.ToString(), ct);
            if (providerResult == null)
            {
                result.IsChanged = changed;
                return result;
            }
            providerStr = providerResult;
        }

        if (providerStr == "ElevenLabs")
        {
            host.AddMessage("[yellow]  ElevenLabs TTS is not yet supported.[/]");
            result.IsChanged = changed;
            return result;
        }

        if (Enum.TryParse<TtsProviderType>(providerStr, out var provider) && provider != config.TtsProvider)
        {
            config.TtsProvider = provider;
            changed = true;
        }

        // ── Provider-specific settings ──
        switch (provider)
        {
            case TtsProviderType.OpenAI:
                var openAiKey = await PromptTextHelper.PromptAsync(host, "OpenAI API key for TTS",
                    config.TtsOpenAiApiKey ?? config.OpenAiApiKey ?? "",
                    _ => true, null,
                    ct, isSecret: true, isEmptyToDefault: true);
                if (openAiKey != null)
                {
                    var newKey = string.IsNullOrWhiteSpace(openAiKey) ? null : openAiKey;
                    if (newKey != config.TtsOpenAiApiKey)
                    {
                        config.TtsOpenAiApiKey = newKey;
                        changed = true;
                    }
                }
                break;

            case TtsProviderType.Edge:
                var subKey = await PromptTextHelper.PromptAsync(host, "Azure TTS subscription key",
                    config.TtsSubscriptionKey ?? "",
                    _ => true, null,
                    ct, isSecret: true, isEmptyToDefault: true);
                if (subKey != null)
                {
                    var newKey = string.IsNullOrWhiteSpace(subKey) ? null : subKey;
                    if (newKey != config.TtsSubscriptionKey)
                    {
                        config.TtsSubscriptionKey = newKey;
                        changed = true;
                    }
                }
                var region = await PromptTextHelper.PromptAsync(host, "Azure TTS region",
                    config.TtsRegion ?? "eastus",
                    _ => true, null,
                    ct);
                if (region != null && region != config.TtsRegion)
                {
                    config.TtsRegion = region;
                    changed = true;
                }
                break;

            case TtsProviderType.Coqui:
                var coquiModelPath = await PromptTextHelper.PromptAsync(host, "Path to Coqui model file",
                    config.CoquiModelPath ?? "",
                    _ => true, null,
                    ct, isEmptyToDefault: true);
                if (coquiModelPath != null)
                {
                    var newPath = string.IsNullOrWhiteSpace(coquiModelPath) ? null : coquiModelPath;
                    if (newPath != config.CoquiModelPath)
                    {
                        config.CoquiModelPath = newPath;
                        changed = true;
                    }
                }
                goto case TtsProviderType.Python;

            case TtsProviderType.Python:
                var pythonPath = await PromptTextHelper.PromptAsync(host, "Python path",
                    config.PythonPath ?? "python",
                    _ => true, null,
                    ct);
                if (pythonPath != null && pythonPath != config.PythonPath)
                {
                    config.PythonPath = pythonPath;
                    changed = true;
                }
                var coquiModel = await PromptTextHelper.PromptAsync(host, "Coqui model name",
                    config.CoquiModelName ?? "tts_models/multilingual/mxtts/vits",
                    _ => true, null,
                    ct);
                if (coquiModel != null && coquiModel != config.CoquiModelName)
                {
                    config.CoquiModelName = coquiModel;
                    changed = true;
                }
                break;

            case TtsProviderType.Piper:
                var piperPath = await PromptTextHelper.PromptAsync(host, "Piper binary path",
                    config.PiperPath ?? "piper",
                    _ => true, null,
                    ct);
                if (piperPath != null && piperPath != config.PiperPath)
                {
                    config.PiperPath = piperPath;
                    changed = true;
                }
                var piperModel = await PromptTextHelper.PromptAsync(host, "Piper model path",
                    config.PiperModelPath ?? "",
                    _ => true, null,
                    ct, isEmptyToDefault: true);
                if (piperModel != null)
                {
                    var newPath = string.IsNullOrWhiteSpace(piperModel) ? null : piperModel;
                    if (newPath != config.PiperModelPath)
                    {
                        config.PiperModelPath = newPath;
                        changed = true;
                    }
                }
                break;
        }

        // ── Voice (optional) ──
        var voice = await PromptTextHelper.PromptAsync(host, "Voice name (optional)",
            config.TtsVoice ?? "",
            _ => true, null,
            ct, isEmptyToDefault: true);
        if (voice != null)
        {
            var newVoice = string.IsNullOrWhiteSpace(voice) ? null : voice;
            if (newVoice != config.TtsVoice)
            {
                config.TtsVoice = newVoice;
                changed = true;
            }
        }

        // ── TTS Output Mode ──
        var ttsModes = new (string Name, string Value)[]
        {
            ("Always on", "always-on"),
            ("SISO (single-in-single-out)", "siso"),
            ("Off", "off"),
        };
        var ttsMode = await PromptSelectionHelper.PromptStringAsync(host,
            "TTS output mode:", ttsModes, config.TtsOutputMode, allowCancel: false, cancellationToken: ct);
        if (ttsMode != config.TtsOutputMode)
        {
            config.TtsOutputMode = ttsMode;
            changed = true;
        }

        result.IsChanged = changed;
        return result;
    }
}

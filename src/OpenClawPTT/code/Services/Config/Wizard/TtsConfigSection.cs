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

    public async Task<bool> RunAsync(IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct)
    {
        bool changed = false;

        // ── On initial setup: ask Yes/Skip ──
        if (isInitialSetup)
        {
            var setupTts = await PromptSelectionHelper.PromptBoolAsync(host,
                "Setup Text-To-Speech?", defaultValue: true, allowCancel: false, ct);
            if (!setupTts)
            {
                host.AddMessage("[grey]  Skipped TTS setup.[/]");
                return false;
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
                "Choose TTS provider:", providers, config.TtsProvider.ToString(), allowCancel: false, ct);
        }
        else
        {
            var providerResult = await PromptSelectionHelper.PromptStringWithBackAsync(host,
                "Choose TTS provider:", providers, config.TtsProvider.ToString(), ct);
            if (providerResult == null)
                return changed;
            providerStr = providerResult;
        }

        if (providerStr == "ElevenLabs")
        {
            host.AddMessage("[yellow]  ElevenLabs TTS is not yet supported.[/]");
            return changed;
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
                    ct, isSecret: true, allowEmpty: true);
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
                    ct, isSecret: true, allowEmpty: true);
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
                    ct, allowEmpty: true);
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
            ct, allowEmpty: true);
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
        var ttsMode = await PromptSelectionHelper.PromptEnumAsync<TtsOutputMode>(host,
            "TTS output mode:", ParseTtsOutputMode(config.TtsOutputMode), allowCancel: false, ct);
        var ttsModeStr = ttsMode.ToString().ToLowerInvariant();
        if (ttsModeStr != config.TtsOutputMode)
        {
            config.TtsOutputMode = ttsModeStr;
            changed = true;
        }

        return changed;
    }

    private static TtsOutputMode ParseTtsOutputMode(string value) => value.ToLowerInvariant() switch
    {
        "always-on" => TtsOutputMode.AlwaysOn,
        "siso" => TtsOutputMode.Siso,
        "off" => TtsOutputMode.Off,
        _ => TtsOutputMode.Siso,
    };

    private enum TtsOutputMode { AlwaysOn, Siso, Off }
}

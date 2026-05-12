using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using StreamShell;

namespace OpenClawPTT.ConfigWizard;

/// <summary>Configures Speech-To-Text settings.</summary>
public sealed class SttConfigSection : IConfigSectionWizard
{
    public string Name => "Speech-To-Text";
    public string Description => "STT provider and transcription settings";

    public async Task<bool> RunAsync(IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct)
    {
        bool changed = false;

        // ── On initial setup: ask Yes/Skip ──
        if (isInitialSetup)
        {
            var setupStt = await PromptSelectionHelper.PromptSkipOrProceedAsync(host,
                "Setup Speech-To-Text?", allowCancel: true, cancellationToken: ct);
            if (!setupStt.HasValue || !setupStt.Value)
            {
                host.AddMessage("[grey]  Skipped STT setup.[/]");
                return false;
            }
        }

        ConfigSelectionHelper.PrintSubSection(host, "proceeding");

        // ── Provider selection ──
        var providers = new (string Name, string Value)[]
        {
            ("Groq", "groq"),
            ("OpenAI", "openai"),
            ("Whisper.cpp (local)", "whisper-cpp"),
        };

        string? provider = await PromptSelectionHelper.PromptStringAsync(host,
            "Choose STT provider:", providers, cancellationToken: ct);

        if (provider == null)
            return false;

        ConfigSelectionHelper.PrintSubSection(host, provider, "");

        if (provider != config.SttProvider)
        {
            config.SttProvider = provider;
            changed = true;
        }

        // ── Provider-specific settings ──
        switch (provider)
        {
            case "groq":
                var groqKey = await PromptTextHelper.PromptAsync(host, "Groq API key (starts with gsk_)",
                    config.GroqApiKey,
                    v => v.StartsWith("gsk_"), "Must start with gsk_",
                    ct, isSecret: true);
                if (groqKey != null && groqKey != config.GroqApiKey)
                {
                    config.GroqApiKey = groqKey;
                    changed = true;
                }
                var groqModel = await PromptTextHelper.PromptAsync(host, "Groq STT model",
                    config.GroqModel ?? "whisper-large-v3",
                    _ => true, null,
                    ct);
                if (groqModel != null && groqModel != config.GroqModel)
                {
                    config.GroqModel = groqModel;
                    changed = true;
                }
                break;

            case "openai":
                var openAiKey = await PromptTextHelper.PromptAsync(host, "OpenAI API key for STT",
                    config.OpenAiApiKey ?? "",
                    _ => true, null,
                    ct, isSecret: true, isEmptyToDefault: true);
                if (openAiKey != null)
                {
                    var newKey = string.IsNullOrWhiteSpace(openAiKey) ? null : openAiKey;
                    if (newKey != config.OpenAiApiKey)
                    {
                        config.OpenAiApiKey = newKey;
                        changed = true;
                    }
                }
                var openAiModel = await PromptTextHelper.PromptAsync(host, "OpenAI STT model",
                    config.OpenAiModel ?? "whisper-1",
                    _ => true, null,
                    ct);
                if (openAiModel != null && openAiModel != config.OpenAiModel)
                {
                    config.OpenAiModel = openAiModel;
                    changed = true;
                }
                break;

            case "whisper-cpp":
                var whisperPath = await PromptTextHelper.PromptAsync(host, "Path to whisper-cpp executable",
                    config.WhisperCppPath ?? "",
                    _ => true, null,
                    ct, isEmptyToDefault: true);
                if (whisperPath != null)
                {
                    var newPath = string.IsNullOrWhiteSpace(whisperPath) ? null : whisperPath;
                    if (newPath != config.WhisperCppPath)
                    {
                        config.WhisperCppPath = newPath;
                        changed = true;
                    }
                }
                var whisperModel = await PromptTextHelper.PromptAsync(host, "Path to whisper-cpp model file",
                    config.WhisperCppModelPath ?? "",
                    _ => true, null,
                    ct, isEmptyToDefault: true);
                if (whisperModel != null)
                {
                    var newPath = string.IsNullOrWhiteSpace(whisperModel) ? null : whisperModel;
                    if (newPath != config.WhisperCppModelPath)
                    {
                        config.WhisperCppModelPath = newPath;
                        changed = true;
                    }
                }
                break;
        }

        // ── Locale ──
        var locale = await PromptTextHelper.PromptAsync(host, "Locale (e.g. en-US, ja-JP, ru-RU)",
            config.Locale,
            v => v.Length >= 2, "At least 2 characters",
            ct);
        if (locale != null && locale != config.Locale)
        {
            config.Locale = locale;
            changed = true;
        }

        return changed;
    }
}

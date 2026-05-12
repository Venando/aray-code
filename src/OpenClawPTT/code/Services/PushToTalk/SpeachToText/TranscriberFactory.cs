using System;
using OpenClawPTT;
using OpenClawPTT.Services;

namespace OpenClawPTT.Transcriber;

/// <summary>
/// Factory for creating ITranscriber instances based on configuration.
/// </summary>
public static class TranscriberFactory
{
    public static ITranscriber Create(AppConfig config, IColorConsole colorConsole)
    {
        return config.SttProvider?.ToLowerInvariant() switch
        {
            "groq" => new GroqTranscriberAdapter(
                config.GroqApiKey,
                config.GroqModel ?? "whisper-large-v3-turbo",
                config.GroqRetryCount,
                config.GroqRetryDelayMs,
                config.GroqRetryBackoffFactor),

            "openai" => new OpenAiTranscriberAdapter(
                config.OpenAiApiKey ?? throw new ArgumentNullException(nameof(config.OpenAiApiKey), "OpenAI API key is required for OpenAI provider"),
                config.OpenAiModel ?? "whisper-1"),

            "whisper-cpp" => CreateWhisperCpp(config, colorConsole.GetStreamShellHost()),

            null or "" => new GroqTranscriberAdapter(
                config.GroqApiKey,
                config.GroqModel ?? "whisper-large-v3-turbo",
                config.GroqRetryCount,
                config.GroqRetryDelayMs,
                config.GroqRetryBackoffFactor),

            _ => throw new ArgumentException($"Unknown STT provider: {config.SttProvider}")
        };
    }

    /// <summary>
    /// Creates a WhisperCppTranscriberAdapter using the WhisperCppModelManager
    /// and the configured model name. The model must be downloaded before use.
    /// </summary>
    private static ITranscriber CreateWhisperCpp(AppConfig config, IStreamShellHost host)
    {
        var modelManager = new WhisperCppModelManager(host, config.CustomDataDir ?? config.DataDir);
        var modelName = config.WhisperCppModel ?? "base";

        // If the model isn't downloaded yet, this will throw on first transcription
        // (the user should have downloaded it during config/reconfigure)
        return new WhisperCppTranscriberAdapter(modelManager, modelName);
    }
}

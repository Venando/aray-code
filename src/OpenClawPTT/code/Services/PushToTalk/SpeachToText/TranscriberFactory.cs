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
                config.OpenAiApiKey ?? throw new InvalidOperationException("OpenAI API key is required for OpenAI STT provider"),
                config.OpenAiModel ?? "whisper-1"),

            "whisper-cpp" => CreateWhisperCpp(config, colorConsole.GetStreamShellHost() ?? throw new InvalidOperationException("Cannot initialize whisper-cpp STT: terminal integration unavailable.")),

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

        return new WhisperCppTranscriberAdapter(modelManager, modelName,
            whisperBinaryPath: config.WhisperCppBinaryPath);
    }
}

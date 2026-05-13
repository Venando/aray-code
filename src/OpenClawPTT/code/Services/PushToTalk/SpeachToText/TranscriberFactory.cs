using System;
using System.IO;
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
        return (string.IsNullOrEmpty(config.SttProvider) ? AppConfig.ProviderGroq : config.SttProvider).ToLowerInvariant() switch
        {
            AppConfig.ProviderGroq => new GroqTranscriberAdapter(
                config.GroqApiKey,
                config.GroqModel!,  // null-safe: constructors handle null internally
                config.GroqRetryCount,
                config.GroqRetryDelayMs,
                config.GroqRetryBackoffFactor),

            AppConfig.ProviderOpenAi => new OpenAiTranscriberAdapter(
                config.OpenAiApiKey ?? throw new InvalidOperationException("OpenAI API key is required for OpenAI STT provider"),
                config.OpenAiModel!),  // null-safe: constructors handle null internally

            AppConfig.ProviderWhisperCpp => CreateWhisperCpp(config, colorConsole.GetStreamShellHost()
                ?? throw new InvalidOperationException("Cannot initialize whisper-cpp STT: terminal integration unavailable.")),

            AppConfig.ProviderFasterWhisper => CreateFasterWhisper(config, colorConsole.GetStreamShellHost()
                ?? throw new InvalidOperationException("Cannot initialize faster-whisper STT: terminal integration unavailable.")),

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

    /// <summary>
    /// Creates a <see cref="FasterWhisperTranscriberAdapter"/> using <c>uv</c> + <c>faster-whisper</c>.
    /// No Python path, packages, or binary path configuration required — <c>uv</c>
    /// handles the full toolchain automatically.
    /// </summary>
    private static ITranscriber CreateFasterWhisper(AppConfig config, IStreamShellHost host)
    {
        var env = new FasterWhisperEnvironment(config.CustomDataDir ?? config.DataDir);
        var modelManager = new FasterWhisperModelManager(env, host);
        var modelName = config.FasterWhisperModel ?? "base";

        return new FasterWhisperTranscriberAdapter(env, modelManager, modelName);
    }
}

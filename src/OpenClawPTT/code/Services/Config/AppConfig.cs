using System.Text.Json.Serialization;
using OpenClawPTT.Services;
using OpenClawPTT.TTS;

namespace OpenClawPTT;

public enum ThinkingMode
{
    /// <summary>Display nothing.</summary>
    None = 0,
    /// <summary>Show a thinking emoji like a tool call with empty args.</summary>
    Emoji = 1,
    /// <summary>Show first N lines of thinking, tool-output style.</summary>
    FirstNLines = 2,
    /// <summary>Show all thinking, agent-reply style (supports future streaming).</summary>
    Full = 3,
}

public sealed class AppConfig
{
    public string GatewayUrl { get; set; } = "ws://localhost:18789";
    public string? AuthToken { get; set; }
    public string? DeviceToken { get; set; }
    public string? TlsFingerprint { get; set; }
    public string Locale { get; set; } = "en-US";
    public int SampleRate { get; set; } = 16_000;
    public int Channels { get; set; } = 1;
    public int BitsPerSample { get; set; } = 16;
    public int MaxRecordSeconds { get; set; } = 120;
    /// <summary>Controls diagnostic log output verbosity. Default = Error (only errors shown).</summary>
    public LogLevel DebugLevel { get; set; } = LogLevel.Error;
    public string GroqApiKey { get; set; } = "";
    public bool RealTimeReplyOutput => ReplyDisplayMode is ReplyDisplayMode.Delta or ReplyDisplayMode.Both;
    public ReplyDisplayMode ReplyDisplayMode { get; set; } = ReplyDisplayMode.Both;

    // STT Provider configuration
    public string? SttProvider { get; set; } // "groq", "openai", "whisper-cpp", null = default to groq
    public string? OpenAiApiKey { get; set; }
    public string? OpenAiModel { get; set; }
    public string? WhisperCppModel { get; set; }
    public string? GroqModel { get; set; }

    // Shortcut settings
    public string HotkeyCombination { get; set; } = "Alt+=";
    public bool HoldToTalk { get; set; } = false;
    public ThinkingMode ThinkingDisplayMode { get; set; } = ThinkingMode.FirstNLines;
    public int ThinkingPreviewLines { get; set; } = 1;
    public int HistoryDisplayCount { get; set; } = 8;
    public string? LastActiveAgentId { get; set; }
    public bool RequireConfirmBeforeSend { get; set; } = false;


    public string TranscriptionPromptPrefix { get; set; } = "";
    // AudioWrapPrompt and IsAudioEnabled removed — no longer needed
    public int GroqRetryCount { get; set; } = 0;
    public int GroqRetryDelayMs { get; set; } = 1000;
    public double GroqRetryBackoffFactor { get; set; } = 2.0;
    public double ReconnectDelaySeconds { get; set; } = 1.5;

    // Bottom panel settings
    public int BottomPanelLineCount { get; set; } = 2; // Maximum height of the agent status bottom panel

    // Text formatting
    public int RightMarginIndent { get; set; } = 5; // Minimum right margin indent in characters
    public bool EnableWordWrap { get; set; } = true; // Enable word wrapping and margin indent
    public string UserMessagePrefix { get; set; } = "[green] Me:[/] ";

    /// <summary>The right-edge margin reserved for display, computed once after loading config.
    /// Equals max(RightMarginIndent, 10% of console width).</summary>
    [JsonIgnore]
    public int ReservedRightMargin { get; set; } = 10;

    // Status bar positions for each status part
    public DisplayPosition ActiveAgentPosition { get; set; } = DisplayPosition.TopSeparatorLeft;
    public DisplayPosition ModelPosition { get; set; } = DisplayPosition.TopSeparatorLeft;
    public DisplayPosition ThinkingLevelPosition { get; set; } = DisplayPosition.TopSeparatorLeft;
    public DisplayPosition ContextPosition { get; set; } = DisplayPosition.TopSeparatorLeft;
    public DisplayPosition ConversationNamePosition { get; set; } = DisplayPosition.TopSeparatorLeft;
    public DisplayPosition ConnectionStatusPosition { get; set; } = DisplayPosition.TopSeparatorRight;
    public DisplayPosition DirectLlmPosition { get; set; } = DisplayPosition.TopSeparatorRight;

    // Visual feedback settings
    public VisualMode VisualMode { get; set; } = VisualMode.SolidDot;

    // Visual feedback settings
    public bool VisualFeedbackEnabled { get; set; } = true;
    public string VisualFeedbackPosition { get; set; } = "TopRight";
    public int VisualFeedbackSize { get; set; } = 20;
    public double VisualFeedbackOpacity { get; set; } = 1.0;
    public string VisualFeedbackColor { get; set; } = "#FF0000";
    public int VisualFeedbackRimThickness { get; set; } = 8;

    // TTS settings
    public TtsProviderType TtsProvider { get; set; } = TtsProviderType.OpenAI;
    public string? TtsOpenAiApiKey { get; set; }
    public string? TtsSubscriptionKey { get; set; }
    public string? TtsRegion { get; set; }
    public string? TtsVoice { get; set; }  // Provider-specific voice name
    public string? TtsModel { get; set; }  // Provider-specific model

    // Coqui TTS settings
    public string? CoquiModelPath { get; set; }
    public string? CoquiModelName { get; set; }
    public string? CoquiConfigPath { get; set; }
    public string? PythonPath { get; set; }

    // Piper TTS settings
    public string? PiperPath { get; set; }
    public string? PiperModelPath { get; set; }
    public string? PiperVoice { get; set; }

    // eSpeak NG path for Coqui TTS (platform-specific default)
    public string? EspeakNgPath { get; set; }



    // Direct LLM settings (bypass agent for direct LLM calls)
    public string? DirectLlmToken { get; set; }
    public string? DirectLlmUrl { get; set; }
    public string? DirectLlmModelName { get; set; }
    public string DirectLlmApiType { get; set; } = "openai-completions"; // "openai-completions" or "anthropic-messages"

    // Audio response settings

    public string? TtsApiKey { get; set; } // Optional ElevenLabs API key
    public string? TtsVoiceId { get; set; } // Default ElevenLabs voice

    // TTS SISO settings
    public string TtsOutputMode { get; set; } = "siso"; // "always-on", "siso", "off"
    public int TtsDirectMaxChars { get; set; } = 300;   // Under this: speak directly
    public int TtsMaxChars { get; set; } = 1500;        // Upper limit for TTS output
    public string TtsCodeBlockMode { get; set; } = "smart"; // "summarize", "skip", "smart"
    public string TtsTooLongFallback { get; set; } = "truncate"; // "truncate" or "skip"
    public bool TtsUseDirectLlmSummary { get; set; } = true;

    // Conversation naming prompt (Direct LLM)
    public string ConversationNamingPrompt { get; set; } =
        "Give a very short 4-6 word descriptive name for a conversation that starts with this message. Return ONLY the name, no quotes, no explanation, no punctuation at the end.\n\nMessage: {message}";

    [JsonIgnore]
    public string ClientVersion => "1.0.0";

    [JsonIgnore]
    public string? CustomDataDir { get; set; }

    [JsonIgnore]
    public string DataDir => CustomDataDir
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".openclaw-ptt");

    /// <summary>Short plain-English descriptions for each config property, shown by /config.</summary>
    [JsonIgnore]
    public static readonly IReadOnlyDictionary<string, string> PropertyDescriptions = new Dictionary<string, string>
    {
        ["GatewayUrl"] = "WebSocket URL for the OpenClaw Gateway",
        ["AuthToken"] = "Gateway authentication token",
        ["DeviceToken"] = "Device identifier for gateway auth",
        ["TlsFingerprint"] = "TLS fingerprint for gateway connection",
        ["Locale"] = "Language/region for speech recognition",
        ["SampleRate"] = "Microphone sample rate in Hz",
        ["Channels"] = "Number of audio channels (1 = mono)",
        ["BitsPerSample"] = "Audio sample bit depth",
        ["MaxRecordSeconds"] = "Maximum recording duration in seconds",
        ["DebugLevel"] = "Log verbosity: None, Error, Info, Debug, Verbose",
        ["GroqApiKey"] = "Groq STT API key",
        ["RealTimeReplyOutput"] = "Show agent replies in real-time during recording",
        ["ReplyDisplayMode"] = "Where to show replies: Console, Audio, Both",
        ["SttProvider"] = "STT provider: groq, openai, whisper-cpp",
        ["OpenAiApiKey"] = "OpenAI API key (for STT or Direct LLM)",
        ["OpenAiModel"] = "OpenAI model name for STT",
        ["WhisperCppModel"] = "Whisper.cpp model name (e.g. base, small.en, medium)",
        ["GroqModel"] = "Groq model for STT",
        ["HotkeyCombination"] = "Push-to-talk hotkey (e.g. Alt+=)",
        ["HoldToTalk"] = "Hold key to record, release to send (vs toggle)",
        ["ThinkingDisplayMode"] = "How to show agent thinking: Off, FirstNLines, Summary, Full",
        ["ThinkingPreviewLines"] = "Rows of thinking to show in FirstNLines mode",
        ["LastActiveAgentId"] = "Last active agent ID to restore on reconnect or restart",
        ["HistoryDisplayCount"] = "Number of previous messages to show on connect",
        ["RequireConfirmBeforeSend"] = "Ask for confirmation before sending messages",

        ["TranscriptionPromptPrefix"] = "Prefix for STT transcription text sent to agent",
        ["GroqRetryCount"] = "STT retry attempts on failure",
        ["GroqRetryDelayMs"] = "Delay between STT retries in ms",
        ["GroqRetryBackoffFactor"] = "Backoff multiplier for STT retry delay",
        ["BottomPanelLineCount"] = "Maximum height of the agent status bottom panel",
        ["ReconnectDelaySeconds"] = "Gateway reconnection delay in seconds",
        ["RightMarginIndent"] = "Right margin indent for word-wrap in characters",
        ["EnableWordWrap"] = "Enable word-wrap and right margin",
        ["UserMessagePrefix"] = "Prefix for user's own messages (Spectre markup, e.g. ' [green] You:[/] ')",
        ["VisualMode"] = "Recording indicator visual style: Dot, SolidDot, Bar",
        ["VisualFeedbackEnabled"] = "Show visual recording indicator",
        ["VisualFeedbackPosition"] = "Indicator position: TopRight, TopLeft, BottomRight, BottomLeft",
        ["VisualFeedbackSize"] = "Indicator size in pixels",
        ["VisualFeedbackOpacity"] = "Indicator opacity (0.0 - 1.0)",
        ["VisualFeedbackColor"] = "Indicator hex color (e.g. #FF0000)",
        ["VisualFeedbackRimThickness"] = "Indicator border thickness in pixels",
        ["ActiveAgentPosition"] = "Status bar position for active agent icon+name (None, TopSeparatorLeft, TopSeparatorRight, BottomSeparatorLeft, BottomSeparatorRight)",
        ["ModelPosition"] = "Status bar position for model name",
        ["ThinkingLevelPosition"] = "Status bar position for thinking level",
        ["ContextPosition"] = "Status bar position for token context usage",
        ["ConversationNamePosition"] = "Status bar position for conversation name",
        ["ConnectionStatusPosition"] = "Status bar position for GW/TTS connection status",
        ["DirectLlmPosition"] = "Status bar position for direct LLM status",
        ["TtsProvider"] = "TTS provider: OpenAI, ElevenLabs, Azure, Coqui, Piper, Espeak",
        ["TtsOpenAiApiKey"] = "OpenAI API key for TTS",
        ["TtsSubscriptionKey"] = "Azure TTS subscription key",
        ["TtsRegion"] = "Azure TTS region",
        ["TtsVoice"] = "Voice name for TTS",
        ["TtsModel"] = "Model for TTS",
        ["CoquiModelPath"] = "Path to Coqui TTS model file",
        ["CoquiModelName"] = "Coqui TTS model name",
        ["CoquiConfigPath"] = "Path to Coqui TTS config",
        ["PythonPath"] = "Path to Python interpreter for TTS",
        ["PiperPath"] = "Path to Piper TTS binary",
        ["PiperModelPath"] = "Path to Piper TTS voice model",
        ["PiperVoice"] = "Piper voice name",
        ["EspeakNgPath"] = "Path to eSpeak NG installation",
        ["DirectLlmToken"] = "API token for direct LLM access",
        ["DirectLlmUrl"] = "API URL for direct LLM",
        ["DirectLlmModelName"] = "Model name for direct LLM",
        ["DirectLlmApiType"] = "LLM API type: openai-completions or anthropic-messages",

        ["TtsApiKey"] = "ElevenLabs TTS API key",
        ["TtsVoiceId"] = "ElevenLabs voice ID",
        ["TtsOutputMode"] = "TTS mode: always-on, siso (single-in-single-out), off",
        ["TtsDirectMaxChars"] = "Max chars for direct TTS (no summary)",
        ["TtsMaxChars"] = "Upper limit for TTS output; longer gets skipped/summarized",
        ["TtsCodeBlockMode"] = "How to handle code in TTS: summarize, skip, smart",
        ["TtsTooLongFallback"] = "Action when TTS exceeds limit: truncate or skip",
        ["TtsUseDirectLlmSummary"] = "Use direct LLM summary instead of TTS summarizer pipeline",
        ["CustomDataDir"] = "Override for config data directory path",
        ["ConversationNamingPrompt"] = "Prompt template for generating conversation names via Direct LLM. Use {message} as placeholder for the user's first message.",
    };
}
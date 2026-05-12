using System.Linq;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using Xunit;

namespace OpenClawPTT.Tests.Config;

public class ConfigurationWizardTests
{
    [Fact]
    public async Task RunSetupAsync_SendsGatewayUrlPrompt()
    {
        var host = new FakeStreamShellHost();
        var wizard = new ConfigurationWizard();

        _ = wizard.RunSetupAsync(host);

        // First message is the prompt description + the current value hint (AppConfig has defaults)
        Assert.Equal(2, host.Messages.Count);
        Assert.Contains("▸ Gateway URL", host.Messages[0]);
        Assert.Contains("[cyan2]", host.Messages[0]);
    }

    [Fact]
    public async Task RunSetupAsync_ValidInputs_CompletesWithConfig()
    {
        var host = new FakeStreamShellHost();
        var wizard = new ConfigurationWizard();

        var task = wizard.RunSetupAsync(host);

        host.SubmitInput("wss://localhost:18789");  // GatewayUrl
        host.SubmitInput("my-token");              // AuthToken
        host.SubmitInput("sha256/abc123");         // TlsFingerprint
        host.SubmitInput("gsk_testkey123");        // GroqApiKey
        host.SubmitInput("en-US");                 // Locale
        host.SubmitInput("16000");                 // SampleRate
        host.SubmitInput("120");                   // MaxRecordSeconds
        host.SubmitInput("true");                  // RealTimeReplyOutput
        host.SubmitInput("Alt+=");                 // HotkeyCombination
        host.SubmitInput("false");                 // HoldToTalk
        host.SubmitInput("[Transcribe]:");         // TranscriptionPromptPrefix
        host.SubmitInput("true");                  // VisualFeedbackEnabled
        host.SubmitInput("TopRight");              // VisualFeedbackPosition
        host.SubmitInput("20");                    // VisualFeedbackSize
        host.SubmitInput("1.0");                   // VisualFeedbackOpacity
        host.SubmitInput("#FF0000");               // VisualFeedbackColor
        host.SubmitInput("8");                     // VisualFeedbackRimThickness
        host.SubmitInput("eleven-key");            // TtsApiKey
        host.SubmitInput("voice123");              // TtsVoiceId

        var config = await task;

        Assert.NotNull(config);
        Assert.Equal("wss://localhost:18789", config.GatewayUrl);
        Assert.Equal("my-token", config.AuthToken);
        Assert.Equal("sha256/abc123", config.TlsFingerprint);
        Assert.Equal("gsk_testkey123", config.GroqApiKey);
        Assert.Equal("en-US", config.Locale);
        Assert.Equal(16000, config.SampleRate);
        Assert.Equal(120, config.MaxRecordSeconds);
        Assert.True(config.RealTimeReplyOutput);
        Assert.Equal("Alt+=", config.HotkeyCombination);
        Assert.False(config.HoldToTalk);
        Assert.Equal("[Transcribe]:", config.TranscriptionPromptPrefix);
        Assert.True(config.VisualFeedbackEnabled);
        Assert.Equal("TopRight", config.VisualFeedbackPosition);
        Assert.Equal(20, config.VisualFeedbackSize);
        Assert.Equal(1.0, config.VisualFeedbackOpacity);
        Assert.Equal("#FF0000", config.VisualFeedbackColor);
        Assert.Equal(8, config.VisualFeedbackRimThickness);
        Assert.Equal("eleven-key", config.TtsApiKey);
        Assert.Equal("voice123", config.TtsVoiceId);
    }

    [Fact]
    public async Task RunSetupAsync_InvalidInput_ResendsPrompt()
    {
        var host = new FakeStreamShellHost();
        var wizard = new ConfigurationWizard();

        var task = wizard.RunSetupAsync(host);

        // First prompt: description + current value hint (AppConfig has default GatewayUrl)
        Assert.Equal(2, host.Messages.Count);
        var firstPromptDescription = host.Messages[0];
        var firstPromptHint = host.Messages[1];

        // Submit invalid input
        host.SubmitInput("not-a-valid-url");

        // Should have: validation error + repeated prompt (blank separator + description + hint)
        Assert.Equal(6, host.Messages.Count);
        Assert.Contains("[red]  ✗ Invalid value.", host.Messages[2]);
        Assert.Equal("", host.Messages[3]); // blank separator
        Assert.Equal(firstPromptDescription, host.Messages[4]);
        Assert.Equal(firstPromptHint, host.Messages[5]);

        // Now submit valid input to complete the test cleanly
        host.SubmitInput("ws://localhost:18789");
        // AuthToken
        host.SubmitInput("");
        // GroqApiKey
        host.SubmitInput("gsk_testkey123");
        // Locale
        host.SubmitInput("en");
        // SampleRate
        host.SubmitInput("16000");
        // MaxRecordSeconds
        host.SubmitInput("60");
        // RealTimeReplyOutput
        host.SubmitInput("true");
        // HotkeyCombination
        host.SubmitInput("Alt+=");
        // HoldToTalk
        host.SubmitInput("false");
        // TranscriptionPromptPrefix
        host.SubmitInput("prefix");
        // VisualFeedbackEnabled
        host.SubmitInput("true");
        // VisualFeedbackPosition
        host.SubmitInput("TopLeft");
        // VisualFeedbackSize
        host.SubmitInput("10");
        // VisualFeedbackOpacity
        host.SubmitInput("0.5");
        // VisualFeedbackColor
        host.SubmitInput("#00FF00");
        // VisualFeedbackRimThickness
        host.SubmitInput("5");
        host.SubmitInput("text-only");
        // TtsApiKey
        host.SubmitInput("");
        // TtsVoiceId
        host.SubmitInput("");

        var config = await task;
        Assert.NotNull(config);
    }

    [Fact]
    public async Task RunSetupAsync_DoubleDash_ClearsTextField()
    {
        var host = new FakeStreamShellHost();
        var wizard = new ConfigurationWizard();

        // Start with an existing config that has a prefix set
        var existing = new AppConfig
        {
            GatewayUrl = "ws://localhost:18789",
            Locale = "en-US",
            SampleRate = 16000,
            MaxRecordSeconds = 60,
            HotkeyCombination = "Alt+=",
            HoldToTalk = false,
            TranscriptionPromptPrefix = "[It's a raw speech-to-text transcription]:",
        };

        var task = wizard.RunSetupAsync(host, existing);

        host.SubmitInput("ws://localhost:18789");  // GatewayUrl
        host.SubmitInput("");                      // AuthToken
        host.SubmitInput("gsk_testkey123");        // GroqApiKey
        host.SubmitInput("en-US");                 // Locale
        host.SubmitInput("16000");                 // SampleRate
        host.SubmitInput("60");                    // MaxRecordSeconds
        host.SubmitInput("true");                  // RealTimeReplyOutput
        host.SubmitInput("Alt+=");                 // HotkeyCombination
        host.SubmitInput("false");                 // HoldToTalk
        host.SubmitInput("--");                    // TranscriptionPromptPrefix: clear
        host.SubmitInput("true");                  // VisualFeedbackEnabled
        host.SubmitInput("TopLeft");               // VisualFeedbackPosition
        host.SubmitInput("10");                    // VisualFeedbackSize
        host.SubmitInput("0.5");                   // VisualFeedbackOpacity
        host.SubmitInput("#00FF00");               // VisualFeedbackColor
        host.SubmitInput("5");                     // VisualFeedbackRimThickness
        host.SubmitInput("");                      // TtsApiKey
        host.SubmitInput("");                      // TtsVoiceId

        var config = await task;

        Assert.NotNull(config);
        Assert.Equal("", config.TranscriptionPromptPrefix);
    }
}

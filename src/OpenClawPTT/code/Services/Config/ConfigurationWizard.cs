using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using Spectre.Console;

namespace OpenClawPTT;

public sealed class ConfigurationWizard
{
    /// <summary>Set to true while the wizard is active, so main input handler skips processing.</summary>
    public static bool IsActive { get; private set; }

    private enum Step
    {
        GatewayUrl,
        AuthToken,
        TlsFingerprint,
        GroqApiKey,
        Locale,
        SampleRate,
        MaxRecordSeconds,
        RealTimeReplyOutput,
        HotkeyCombination,
        HoldToTalk,
        TranscriptionPromptPrefix,
        VisualFeedbackEnabled,
        VisualFeedbackPosition,
        VisualFeedbackSize,
        VisualFeedbackOpacity,
        VisualFeedbackColor,
        VisualFeedbackRimThickness,
        AudioResponseMode,
        TtsApiKey,
        TtsVoiceId,
        Done
    }

    private static readonly Dictionary<Step, IWizardStepHandler> _handlers = new()
    {
        [Step.GatewayUrl] = new GatewayUrlHandler(),
        [Step.GroqApiKey] = new GroqApiKeyHandler(),
        [Step.HotkeyCombination] = new HotkeyCombinationHandler(),
        [Step.VisualFeedbackPosition] = new VisualFeedbackPositionHandler(),
        [Step.VisualFeedbackColor] = new VisualFeedbackColorHandler(),
    };

    private static IWizardStepHandler? GetHandler(Step step)
        => _handlers.GetValueOrDefault(step);

    private IStreamShellHost? _host;
    private AppConfig? _existing;
    private AppConfig _config = new();
    private Step _currentStep = Step.GatewayUrl;
    private TaskCompletionSource<AppConfig>? _tcs;
    private CancellationTokenRegistration? _ctReg;
    private bool _isFirstPrompt = true;

    public Task<AppConfig> RunSetupAsync(IStreamShellHost host, AppConfig? existing = null, CancellationToken ct = default)
    {
        _host = host;
        _existing = existing;
        _config = existing != null ? Clone(existing) : new AppConfig();
        _currentStep = Step.GatewayUrl;
        _tcs = new TaskCompletionSource<AppConfig>();
        _isFirstPrompt = true;
        IsActive = true;

        if (ct.CanBeCanceled)
        {
            _ctReg = ct.Register(() =>
            {
                Unsubscribe();
                _tcs.TrySetCanceled(ct);
            });
        }

        host.UserInputSubmitted += OnUserInputSubmitted;
        SendPrompt(Step.GatewayUrl);

        return _tcs.Task;
    }

    private void OnUserInputSubmitted(StreamShell.UserInputSubmittedEventArgs e)
    {
        if (_host == null || _tcs == null)
            return;

        string input = e.TextWithoutAttachments ?? e.RawOutput;
        var rawInput = input.Trim();
        var step = _currentStep;

        // "--" means clear the field (set to empty string)
        if (rawInput == "--")
        {
            if (IsClearableField(step))
            {
                ApplyValue(step, "");
                _host.AddMessage("[green]  ✓ (cleared)[/]");
                Advance();
                return;
            }
        }

        // Empty input accepts the default value (unless it's an optional blank field)
        if (string.IsNullOrEmpty(rawInput))
        {
            if (IsOptionalSkipStep(step))
            {
                ApplyValue(step, null!);
                Advance();
                return;
            }

            rawInput = GetDefaultValue(step);
        }

        if (!Validate(step, rawInput, out var parsedValue))
        {
            var hint = GetValidationHint(step);
            _host.AddMessage($"[red]  ✗ Invalid value.{hint}[/]");
            SendPrompt(step);
            return;
        }

        ApplyValue(step, parsedValue ?? rawInput);

        // Show confirmation of what was set (masked for secrets)
        var displayValue = IsSecretStep(step)
            ? MaskSecret(parsedValue ?? rawInput)
            : parsedValue ?? rawInput;
        _host.AddMessage($"[green]  ✓ {Markup.Escape(displayValue)}[/]");

        Advance();
    }

    private void Advance()
    {
        _currentStep++;

        if (_currentStep == Step.TlsFingerprint)
        {
            // Skip TLS fingerprint prompt if not using wss://
            if (!_config.GatewayUrl.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            {
                _config.TlsFingerprint = null;
                _currentStep++;
            }
        }

        if (_currentStep == Step.Done)
        {
            Unsubscribe();
            _tcs?.TrySetResult(_config);
            return;
        }

        SendPrompt(_currentStep);
    }

    private void Unsubscribe()
    {
        IsActive = false;
        if (_host != null)
        {
            _host.UserInputSubmitted -= OnUserInputSubmitted;
        }
        _ctReg?.Dispose();
        _ctReg = null;
    }

    private void SendPrompt(Step step)
    {
        if (!_isFirstPrompt)
        {
            _host?.AddMessage(""); // blank line between prompts
        }
        _isFirstPrompt = false;

        var description = GetDescription(step);
        var defaultVal = GetDefaultValue(step);
        var hasDefault = !string.IsNullOrEmpty(defaultVal);

        // Show description with a short hint
        _host?.AddMessage($"[cyan2]▸ {Markup.Escape(description)}[/]");

        if (hasDefault)
        {
            var displayDefault = IsSecretStep(step)
                ? MaskSecret(defaultVal)
                : defaultVal;
            _host?.AddMessage($"  [grey](current: {Markup.Escape(displayDefault)}, press Enter to keep)[/]");
        }
    }

    /// <summary>Whether entering nothing skips this optional field entirely.</summary>
    private static bool IsOptionalSkipStep(Step step) => step switch
    {
        Step.TlsFingerprint => true,
        Step.TtsApiKey => true,
        Step.TtsVoiceId => true,
        Step.AuthToken => true,
        _ => false
    };

    /// <summary>Fields that can be cleared by typing -- (set to empty string instead of keeping default).</summary>
    private static bool IsClearableField(Step step) => step switch
    {
        Step.TranscriptionPromptPrefix => true,
        _ => false
    };

    private static bool IsSecretStep(Step step)
    {
        var handler = GetHandler(step);
        if (handler != null)
            return handler.IsSecret;
        return step switch
        {
            Step.AuthToken => true,
            Step.TtsApiKey => true,
            _ => false
        };
    }

    private static string MaskSecret(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "(not set)";
        if (value.Length <= 4)
            return new string('*', value.Length);
        // Show first 4 + mask
        return value[..4] + new string('*', Math.Min(value.Length - 4, 12));
    }

    private static string GetDescription(Step step)
    {
        var handler = GetHandler(step);
        if (handler != null)
            return handler.GetDescription();
        return step switch
        {
            Step.AuthToken => "Auth token (OPENCLAW_GATEWAY_TOKEN env)",
            Step.TlsFingerprint => "TLS cert fingerprint (optional, for wss:// pinning)",
            Step.Locale => "Locale (e.g. en-US, ja-JP, ru-RU)",
            Step.SampleRate => "Audio sample rate (8000–48000 Hz)",
            Step.MaxRecordSeconds => "Max recording length (5–600 seconds)",
            Step.RealTimeReplyOutput => "Real-time reply streaming (true/false)",
            Step.HoldToTalk => "Hold-to-talk (true = hold down, false = toggle)",
            Step.TranscriptionPromptPrefix => "Transcription context prefix (-- to clear)",
            Step.VisualFeedbackEnabled => "Show visual feedback indicator (true/false)",
            Step.VisualFeedbackSize => "Feedback dot size (1–200 pixels)",
            Step.VisualFeedbackOpacity => "Feedback opacity (0.0 transparent – 1.0 solid)",
            Step.VisualFeedbackRimThickness => "Feedback rim (0 = off, 1–50 pixels)",
            Step.TtsApiKey => "ElevenLabs API key (optional blank)",
            Step.TtsVoiceId => "ElevenLabs voice ID",
            _ => step.ToString()
        };
    }

    private static string GetValidationHint(Step step)
    {
        var handler = GetHandler(step);
        if (handler != null)
            return handler.GetValidationHint();
        return step switch
        {
            Step.AuthToken => "",
            Step.TlsFingerprint => "",
            Step.Locale => " At least 2 characters (e.g. en-US)",
            Step.SampleRate => " Expected a number between 8000 and 48000",
            Step.MaxRecordSeconds => " Expected a number between 5 and 600",
            Step.RealTimeReplyOutput => " Expected true or false",
            Step.HoldToTalk => " Expected true or false",
            Step.TranscriptionPromptPrefix => "",
            Step.VisualFeedbackEnabled => " Expected true or false",
            Step.VisualFeedbackSize => " Expected a number between 1 and 200",
            Step.VisualFeedbackOpacity => " Expected a number between 0.0 and 1.0",
            Step.VisualFeedbackRimThickness => " Expected a number between 0 and 50",
            Step.TtsApiKey => "",
            Step.TtsVoiceId => "",
            _ => ""
        };
    }

    private string GetDefaultValue(Step step)
    {
        var handler = GetHandler(step);
        if (handler != null)
            return handler.GetDefaultValue(_config);
        return step switch
        {
            Step.AuthToken => _config.AuthToken ?? Environment.GetEnvironmentVariable("OPENCLAW_GATEWAY_TOKEN") ?? "",
            Step.TlsFingerprint => _config.TlsFingerprint ?? "",
            Step.Locale => _config.Locale,
            Step.SampleRate => _config.SampleRate.ToString(),
            Step.MaxRecordSeconds => _config.MaxRecordSeconds.ToString(),
            Step.RealTimeReplyOutput => _config.RealTimeReplyOutput.ToString(),
            Step.HoldToTalk => _config.HoldToTalk.ToString(),
            Step.TranscriptionPromptPrefix => _config.TranscriptionPromptPrefix,
            Step.VisualFeedbackEnabled => _config.VisualFeedbackEnabled.ToString(),
            Step.VisualFeedbackPosition => _config.VisualFeedbackPosition,
            Step.VisualFeedbackSize => _config.VisualFeedbackSize.ToString(),
            Step.VisualFeedbackOpacity => _config.VisualFeedbackOpacity.ToString("F2"),
            Step.VisualFeedbackColor => _config.VisualFeedbackColor,
            Step.VisualFeedbackRimThickness => _config.VisualFeedbackRimThickness.ToString(),
            Step.TtsApiKey => _config.TtsApiKey ?? "",
            Step.TtsVoiceId => _config.TtsVoiceId ?? "",
            _ => throw new ArgumentOutOfRangeException(nameof(step), step, null)
        };
    }

    private static bool Validate(Step step, string rawInput, out string? parsedValue)
    {
        parsedValue = rawInput;
        var handler = GetHandler(step);
        if (handler != null)
            return handler.Validate(rawInput, out parsedValue);
        switch (step)
        {
            case Step.AuthToken:
                return true;
            case Step.TlsFingerprint:
                return true;
            case Step.Locale:
                return rawInput.Length >= 2;
            case Step.SampleRate:
                return int.TryParse(rawInput, out var n) && n is >= 8000 and <= 48000;
            case Step.MaxRecordSeconds:
                return int.TryParse(rawInput, out var n2) && n2 is >= 5 and <= 600;
            case Step.RealTimeReplyOutput:
                return bool.TryParse(rawInput, out _);
            case Step.HoldToTalk:
                return bool.TryParse(rawInput, out _);
            case Step.TranscriptionPromptPrefix:
                return true; // any input valid (-- to clear)
            case Step.VisualFeedbackEnabled:
                return bool.TryParse(rawInput, out _);
            case Step.VisualFeedbackSize:
                return int.TryParse(rawInput, out var n3) && n3 is >= 1 and <= 200;
            case Step.VisualFeedbackOpacity:
                return double.TryParse(rawInput, out var d) && d >= 0.0 && d <= 1.0;
            case Step.VisualFeedbackRimThickness:
                return int.TryParse(rawInput, out var n4) && n4 is >= 0 and <= 50;
            case Step.TtsApiKey:
                return true;
            case Step.TtsVoiceId:
                return true;
            default:
                throw new ArgumentOutOfRangeException(nameof(step), step, null);
        }
    }

    private void ApplyValue(Step step, string rawInput)
    {
        var handler = GetHandler(step);
        if (handler != null)
        {
            handler.ApplyValue(rawInput, _config);
            return;
        }
        switch (step)
        {
            case Step.AuthToken:
                _config.AuthToken = string.IsNullOrWhiteSpace(rawInput) ? null : rawInput;
                break;
            case Step.TlsFingerprint:
                _config.TlsFingerprint = string.IsNullOrWhiteSpace(rawInput) ? null : rawInput;
                break;
            case Step.Locale:
                _config.Locale = rawInput;
                break;
            case Step.SampleRate:
                _config.SampleRate = int.Parse(rawInput);
                break;
            case Step.MaxRecordSeconds:
                _config.MaxRecordSeconds = int.Parse(rawInput);
                break;
            case Step.HoldToTalk:
                _config.HoldToTalk = bool.Parse(rawInput);
                break;
            case Step.TranscriptionPromptPrefix:
                _config.TranscriptionPromptPrefix = rawInput;
                break;
            case Step.VisualFeedbackEnabled:
                _config.VisualFeedbackEnabled = bool.Parse(rawInput);
                break;
            case Step.VisualFeedbackSize:
                _config.VisualFeedbackSize = int.Parse(rawInput);
                break;
            case Step.VisualFeedbackOpacity:
                _config.VisualFeedbackOpacity = double.Parse(rawInput);
                break;
            case Step.VisualFeedbackRimThickness:
                _config.VisualFeedbackRimThickness = int.Parse(rawInput);
                break;
            case Step.TtsApiKey:
                _config.TtsApiKey = string.IsNullOrWhiteSpace(rawInput) ? null : rawInput;
                break;
            case Step.TtsVoiceId:
                _config.TtsVoiceId = string.IsNullOrWhiteSpace(rawInput) ? null : rawInput;
                break;
        }
    }

    private static AppConfig Clone(AppConfig source)
    {
        // Use JSON round-trip to clone all public properties automatically.
        // This is more maintainable than manually copying 50+ properties.
        // [JsonIgnore] properties like ClientVersion and DataDir are skipped
        // by the serializer, which is correct — they are computed, not stored.
        var json = JsonSerializer.Serialize(source);
        var clone = JsonSerializer.Deserialize<AppConfig>(json);
        clone!.CustomDataDir = source.CustomDataDir;
        return clone;
    }
}

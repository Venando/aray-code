namespace OpenClawPTT;

using OpenClawPTT.Services;
using OpenClawPTT.Services.Diagnostics;

/// <summary>
/// Partial class for config change subscriptions and wiring.
/// </summary>
public partial class AppRunner
{
    /// <summary>
    /// Handles gateway configuration changes: recreates the gateway client and reconnects
    /// when connection-related properties change.
    /// </summary>
    private void HandleGatewayConfigChanged(ConfigChangedEventArgs e, IGatewayService gateway)
    {
        var gwProps = new[]
        {
            nameof(AppConfig.GatewayUrl),
            nameof(AppConfig.AuthToken),
            nameof(AppConfig.DeviceToken),
            nameof(AppConfig.TlsFingerprint),
        };
        if (!e.AnyChanged(gwProps))
            return;

        _statusService.SetServiceStatus(ServiceKind.Gateway, StatusColor.Yellow);
        _console.PrintInfo("Gateway configuration changed — reconnecting...");
        try
        {
            gateway.RecreateWithConfig(e.NewConfig);

            // Fire-and-forget reconnect: don't block the event loop
            _ = Task.Run(async () =>
            {
                try
                {
                    await gateway.ConnectAsync(CancellationToken.None);
                    _console.LogOk("gateway", "Reconnected with new configuration.");
                    _statusService.SetServiceStatus(ServiceKind.Gateway, StatusColor.Green);
                }
                catch (Exception reconnectEx)
                {
                    _console.LogError("gateway", $"Failed to reconnect with new config: {reconnectEx.Message}");
                    _statusService.SetServiceStatus(ServiceKind.Gateway, StatusColor.Red);
                }
            });
        }
        catch (Exception ex)
        {
            _console.LogError("gateway", $"Failed to recreate gateway client: {ex.Message}");
            _statusService.SetServiceStatus(ServiceKind.Gateway, StatusColor.Red);
        }
    }

    /// <summary>
    /// Handles display/UI configuration changes: updates the canonical config reference
    /// and reapplies display/UI config when relevant properties change.
    /// </summary>
    private void HandleDisplayConfigChanged(ConfigChangedEventArgs e)
    {
        var displayProps = new[]
        {
            nameof(AppConfig.UserMessagePrefix),
            nameof(AppConfig.RightMarginIndent),
            nameof(AppConfig.EnableWordWrap),
            nameof(AppConfig.DebugLevel),
            nameof(AppConfig.ThinkingDisplayMode),
            nameof(AppConfig.ThinkingPreviewLines),
            nameof(AppConfig.HistoryDisplayCount),
            nameof(AppConfig.BottomPanelLineCount),
            nameof(AppConfig.VisualMode),
            nameof(AppConfig.VisualFeedbackEnabled),
            nameof(AppConfig.VisualFeedbackPosition),
            nameof(AppConfig.VisualFeedbackSize),
            nameof(AppConfig.VisualFeedbackOpacity),
            nameof(AppConfig.VisualFeedbackColor),
            nameof(AppConfig.VisualFeedbackRimThickness),
        };
        var positionProps = new[]
        {
            nameof(AppConfig.ActiveAgentPosition),
            nameof(AppConfig.ModelPosition),
            nameof(AppConfig.ThinkingLevelPosition),
            nameof(AppConfig.ContextPosition),
            nameof(AppConfig.ConversationNamePosition),
            nameof(AppConfig.ConnectionStatusPosition),
            nameof(AppConfig.TtsStatusPosition),
            nameof(AppConfig.SttStatusPosition),
            nameof(AppConfig.DirectLlmPosition),
            nameof(AppConfig.MainAgentsPosition),
        };

        bool displayChanged = e.AnyChanged(displayProps);
        bool positionsChanged = e.AnyChanged(positionProps);

        if (!displayChanged && !positionsChanged)
            return;

        // Update the canonical config reference so downstream code is fresh
        _cfg = e.NewConfig;

        if (positionsChanged)
            _statusService.ApplyConfigPositions(e.NewConfig);

        if (displayChanged)
            _console.ApplyConsoleConfig(e.NewConfig);
    }

    /// <summary>
    /// Handles STT/audio configuration changes: recreates the transcriber and recorder
    /// when STT provider or audio-recording properties change.
    /// </summary>
    private void HandleSttConfigChanged(ConfigChangedEventArgs e, IAudioService audioService)
    {
        var sttProps = new[]
        {
            nameof(AppConfig.SttProvider),
            nameof(AppConfig.GroqModel),
            nameof(AppConfig.GroqApiKey),
            nameof(AppConfig.OpenAiApiKey),
            nameof(AppConfig.OpenAiModel),
            nameof(AppConfig.WhisperCppModel),
            nameof(AppConfig.WhisperCppBinaryPath),
            nameof(AppConfig.SampleRate),
            nameof(AppConfig.Channels),
            nameof(AppConfig.BitsPerSample),
            nameof(AppConfig.MaxRecordSeconds),
        };
        if (!e.AnyChanged(sttProps))
            return;

        _statusService.SetServiceStatus(ServiceKind.Stt, StatusColor.Yellow);
        try
        {
            // Recorder first (so new params are in place before transcriber is recreated)
            audioService.RecreateRecorder(e.NewConfig, _console);
            audioService.RecreateTranscriber(e.NewConfig, _console);
            _statusService.SetServiceStatus(ServiceKind.Stt, StatusColor.Green);
        }
        catch (Exception ex)
        {
            _statusService.SetServiceStatus(ServiceKind.Stt, StatusColor.Red);
            _console.PrintError($"Failed to update STT: {ex.Message}");
        }
    }
}

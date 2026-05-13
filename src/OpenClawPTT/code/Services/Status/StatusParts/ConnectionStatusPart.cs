namespace OpenClawPTT.Services.StatusParts;

/// <summary>
/// Renders the gateway and TTS connection status, e.g.
/// "GW:● Connected TTS:● Connected".
/// Caches the rendered value so it only rebuilds on actual status changes.
/// </summary>
public sealed class ConnectionStatusPart : StatusPartBase
{
    // Pre-baked constant markup fragments
    private const string GwPrefix = " GW:[";
    private const string TtsPrefix = "]● ";
    private const string TtsSuffix = "[/] TTS:[";
    private const string RightSuffix = "[/]";

    private string _gatewayLabel = "Starting";
    private StatusColor _gatewayColor = StatusColor.Yellow;
    private string _ttsLabel = "Starting";
    private StatusColor _ttsColor = StatusColor.Yellow;

    public ConnectionStatusPart(DisplayPosition defaultPosition = DisplayPosition.TopSeparatorRight, int order = 0)
        : base(defaultPosition, order)
    {
    }

    /// <inheritdoc />
    public override string SeparatorBefore => " ";

    /// <summary>Updates the gateway status. Marks dirty on actual change.</summary>
    public void SetGatewayStatus(string label, StatusColor color)
    {
        if (!string.Equals(_gatewayLabel, label, StringComparison.Ordinal) || _gatewayColor != color)
        {
            _gatewayLabel = label;
            _gatewayColor = color;
            MarkDirty();
        }
    }

    /// <summary>Updates the TTS status. Marks dirty on actual change.</summary>
    public void SetTtsStatus(string label, StatusColor color)
    {
        if (!string.Equals(_ttsLabel, label, StringComparison.Ordinal) || _ttsColor != color)
        {
            _ttsLabel = label;
            _ttsColor = color;
            MarkDirty();
        }
    }

    protected override void BuildText()
    {
        Builder.Append(GwPrefix);
        Builder.Append(ToMarkupColor(_gatewayColor));
        Builder.Append(TtsPrefix);
        Builder.Append(_gatewayLabel);
        Builder.Append(TtsSuffix);
        Builder.Append(ToMarkupColor(_ttsColor));
        Builder.Append(TtsPrefix);
        Builder.Append(_ttsLabel);
        Builder.Append(RightSuffix);
    }
}

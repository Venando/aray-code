using System;

namespace ArayCode.Services.StatusParts;

/// <summary>
/// Renders a single service status as a label prefix with a colored dot, e.g.
/// "GW:●" (green), "TTS:·" (yellow animating), "STT:●" (red).
/// When yellow, the dot animates by cycling through [·, •, ●, •] on each render.
/// </summary>
public sealed class ServiceStatusPart : StatusPartBase
{
    // Animation frames for yellow/transitional state: thin → medium → full → medium
    private static readonly char[] YellowFrames = ['•', '●', '•', '●'];

    private readonly string _label;
    private StatusColor _color = StatusColor.Yellow;
    private int _frameIndex;
    private bool _hidden;

    /// <summary>
    /// Creates a service status part with a label prefix (e.g. "GW:", "TTS:", "STT:").
    /// </summary>
    public ServiceStatusPart(string label, DisplayPosition defaultPosition = DisplayPosition.TopSeparatorRight, int order = 0)
        : base(defaultPosition, order)
    {
        _label = label ?? throw new ArgumentNullException(nameof(label));
    }

    /// <inheritdoc />
    public override string SeparatorBefore => " ";

    /// <summary>The current status color.</summary>
    private StatusColor Color => _color;

    /// <summary>The label prefix (e.g. "GW:", "TTS:").</summary>
    private string Label => _label;

    /// <summary>Gets the current status color.</summary>
    public StatusColor GetCurrentColor() => _color;

    /// <summary>Whether this part is currently in the yellow (transitional) state.</summary>
    internal bool IsYellow => _color == StatusColor.Yellow;

    /// <summary>
    /// Hides or shows this part. When hidden, <see cref="BuildText"/> returns empty
    /// and the part does not appear in the separator.
    /// </summary>
    public void SetHidden(bool hidden)
    {
        if (_hidden != hidden)
        {
            _hidden = hidden;
            MarkDirty();
        }
    }

    /// <summary>Updates the status color. Marks dirty on actual change.</summary>
    public void SetStatus(StatusColor color)
    {
        if (_color != color)
        {
            _color = color;
            UpdateAnimationState();
            MarkDirty();
        }
    }

    protected override void BuildText()
    {
        // When hidden, return empty text — nothing rendered in separator
        if (_hidden)
            return;

        // Leading space before label (e.g. " GW:●") — ensures gap after separator fill
        Builder.Append(' ');
        // Label prefix (e.g. "GW:", "TTS:", "STT:")
        Builder.Append(_label);

        // Colored dot
        string dot = _color == StatusColor.Yellow
            ? YellowFrames[_frameIndex].ToString()
            : "\u25CF"; // ●

        Builder.Append('[');
        Builder.Append(_color.ToMarkupColor());
        Builder.Append(']');
        Builder.Append(dot);
        Builder.Append("[/]");
    }

    private void UpdateAnimationState()
    {
        // Force rebuild on every GetText() when yellow for animation
        AlwaysRebuild = _color == StatusColor.Yellow;
        // Reset frame on state change
        if (_color != StatusColor.Yellow)
            _frameIndex = 0;
    }

    /// <summary>Advances the animation frame. Called externally before render.</summary>
    public void AdvanceFrame()
    {
        if (_color == StatusColor.Yellow)
        {
            _frameIndex = (_frameIndex + 1) % YellowFrames.Length;
            MarkDirty();
        }
    }

}

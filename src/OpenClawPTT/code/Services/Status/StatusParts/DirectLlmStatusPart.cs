using System;
using System.Text;

namespace OpenClawPTT.Services.StatusParts;

/// <summary>
/// Renders the direct LLM status with last-called timestamp, e.g.
/// "LLM:● OK 5s ago" or "LLM:● —".
/// Caches the rendered value so it only rebuilds on actual status changes.
/// </summary>
public sealed class DirectLlmStatusPart : StatusPartBase
{
    // Pre-baked constant markup fragments
    private const string LlmPrefix = " LLM:[";
    private const string StatusPrefix = "]● ";
    private const string RightSuffix = "[/]";

    private string _label = "\u2014"; // em dash — "not configured"
    private StatusColor _color = StatusColor.Yellow;
    private DateTime? _lastCalled;

    public DirectLlmStatusPart(DisplayPosition defaultPosition = DisplayPosition.TopSeparatorRight, int order = 5)
        : base(defaultPosition, order)
    {
    }

    /// <inheritdoc />
    public override string SeparatorBefore => " ";

    /// <summary>Updates the LLM status label and color. Marks dirty on actual change.</summary>
    public void SetStatus(string label, StatusColor color)
    {
        if (!string.Equals(_label, label, StringComparison.Ordinal) || _color != color)
        {
            _label = label;
            _color = color;
            MarkDirty();
        }
    }

    /// <summary>Updates the last-called timestamp. Always marks dirty.</summary>
    public void SetLastCalled(DateTime? timestamp)
    {
        if (_lastCalled != timestamp)
        {
            _lastCalled = timestamp;
            MarkDirty();
        }
    }

    protected override void BuildText()
    {
        Builder.Append(LlmPrefix);
        Builder.Append(ToMarkupColor(_color));
        Builder.Append(StatusPrefix);
        Builder.Append(_label);

        // Show last-called timestamp if available
        if (_lastCalled.HasValue)
        {
            Builder.Append(' ');
            Builder.Append(FormatElapsed(DateTime.Now - _lastCalled.Value));
        }

        Builder.Append(RightSuffix);
    }

    /// <summary>
    /// Formats an elapsed <see cref="TimeSpan"/> as a compact human-readable string
    /// (e.g. "5s", "2m", "1h", "3d").
    /// </summary>
    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 60)
            return $"{(int)elapsed.TotalSeconds}s";
        if (elapsed.TotalMinutes < 60)
            return $"{(int)elapsed.TotalMinutes}m";
        if (elapsed.TotalHours < 24)
            return $"{(int)elapsed.TotalHours}h";
        return $"{(int)elapsed.TotalDays}d";
    }
}
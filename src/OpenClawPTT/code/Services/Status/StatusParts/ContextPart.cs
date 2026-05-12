using System.Text;

namespace OpenClawPTT.Services.StatusParts;

/// <summary>
/// Renders token context usage, e.g. "high · 44% (112k/256k)".
/// Caches the rendered text to avoid re-formatting on every render
/// when the values haven't changed.
/// </summary>
public sealed class ContextPart : StatusPartBase
{
    private long? _contextTokens;
    private long? _totalTokens;

    public ContextPart(DisplayPosition defaultPosition = DisplayPosition.TopSeparatorLeft, int order = 30)
        : base(defaultPosition, order)
    {
    }

    /// <inheritdoc />
    public override string SeparatorBefore => " · ";

    /// <summary>Feeds new token values. Marks dirty on any value change.</summary>
    public void Update(long? contextTokens, long? totalTokens)
    {
        if (_contextTokens != contextTokens || _totalTokens != totalTokens)
        {
            _contextTokens = contextTokens;
            _totalTokens = totalTokens;
            MarkDirty();
        }
    }

    protected override void BuildText()
    {
        if (_contextTokens is null or <= 0 || _totalTokens is null or <= 0)
            return;

        double percent = (double)_totalTokens.Value / _contextTokens.Value * 100.0;

        if (percent < 10.0)
            AppendDouble(Builder, percent, 1);
        else
            AppendDouble(Builder, percent, 0);
        Builder.Append('%');

        Builder.Append(" (");
        AppendTokenCount(Builder, _totalTokens.Value);
        Builder.Append('/');
        AppendTokenCount(Builder, _contextTokens.Value);
        Builder.Append(')');
    }

    private static void AppendDouble(StringBuilder sb, double value, int decimals)
    {
        long scale = decimals == 0 ? 1 : 10;
        long rounded = (long)Math.Round(value * scale, MidpointRounding.AwayFromZero);

        long intPart = rounded / scale;
        long fracPart = rounded % scale;

        sb.Append(intPart);
        if (decimals > 0)
        {
            sb.Append('.');
            sb.Append(fracPart);
        }
    }

    private static void AppendTokenCount(StringBuilder sb, long count)
    {
        if (count >= 1_000_000)
        {
            AppendDouble(sb, (double)count / 1_000_000, 1);
            sb.Append('M');
        }
        else if (count >= 1_000)
        {
            AppendDouble(sb, (double)count / 1_000, 0);
            sb.Append('k');
        }
        else
        {
            sb.Append(count);
        }
    }
}

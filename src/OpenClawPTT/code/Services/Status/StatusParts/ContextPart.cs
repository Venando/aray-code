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
        var display = FormatContextDisplay(_contextTokens, _totalTokens);
        if (display is not null)
            Builder.Append(display);
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

    /// <summary>
    /// Formats the full context display, e.g. "44% (112k/256k)".
    /// Returns null when either value is unavailable or ≤ 0
    /// (matching the original <see cref="BuildText"/> behaviour).
    /// </summary>
    internal static string? FormatContextDisplay(long? contextTokens, long? totalTokens)
    {
        var ctx = contextTokens.GetValueOrDefault();
        var tot = totalTokens.GetValueOrDefault();

        // Original BuildText used OR: if EITHER is missing, show nothing.
        if (ctx <= 0 || tot <= 0)
            return null;

        var sb = new StringBuilder(32);

        double percent = (double)tot / ctx * 100.0;
        if (percent < 10.0)
            AppendDouble(sb, percent, 1);
        else
            AppendDouble(sb, percent, 0);
        sb.Append('%');

        sb.Append(" (");
        AppendTokenCountToBuilder(sb, tot);
        sb.Append('/');
        AppendTokenCountToBuilder(sb, ctx);
        sb.Append(')');

        return sb.ToString();
    }

    /// <summary>
    /// Formats a token count for display, e.g. "1.2M", "112k", "512".
    /// Exposed for reuse by other components (e.g. bottom panel).
    /// </summary>
    internal static string FormatTokenCount(long count)
    {
        var sb = new StringBuilder(8);
        AppendTokenCountToBuilder(sb, count);
        return sb.ToString();
    }

    private static void AppendTokenCountToBuilder(StringBuilder sb, long count)
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

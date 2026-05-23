namespace ArayCode.Services.Themes;

/// <summary>
/// Global mutable holder for the current <see cref="ThemeConfig"/>.
/// Thread-safe via simple reads — write happens only on theme load/swap.
///
/// Consumers (MarkdownToSpectreConverter, ToolDisplayHandler, etc.) read
/// <see cref="Current"/> on each operation, so a runtime theme swap via
/// <c>/theme</c> is reflected immediately without restarting services.
/// </summary>
public static class ThemeProvider
{
    /// <summary>The currently active theme configuration.</summary>
    public static ThemeConfig Current { get; set; } = ThemeConfig.Default;
}

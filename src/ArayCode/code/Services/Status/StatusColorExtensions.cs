using ArayCode.Services.Themes;

namespace ArayCode.Services;

/// <summary>
/// Extension methods for <see cref="StatusColor"/>.
/// </summary>
public static class StatusColorExtensions
{
    /// <summary>
    /// Converts a <see cref="StatusColor"/> to its Spectre.Console markup color name.
    /// </summary>
    public static string ToMarkupColor(this StatusColor color) => color switch
    {
        StatusColor.Green => ThemeProvider.Current.Tools.Messages.Success,
        StatusColor.Yellow => ThemeProvider.Current.Tools.Messages.Warning,
        StatusColor.Red => ThemeProvider.Current.Tools.Messages.Error,
        _ => ThemeProvider.Current.Tools.Messages.Info,
    };
}

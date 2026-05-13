namespace OpenClawPTT.Services;

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
        StatusColor.Green => "green",
        StatusColor.Yellow => "yellow",
        StatusColor.Red => "red",
        _ => "yellow",
    };
}

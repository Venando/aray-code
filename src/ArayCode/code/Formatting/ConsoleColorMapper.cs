namespace ArayCode.Formatting;

/// <summary>
/// Maps System.ConsoleColor values to Spectre.Console color names.
/// Retained for legacy callers; new code should use <c>IToolOutput.Print(text, style)</c>
/// with raw Spectre style strings.
/// </summary>
public static class ConsoleColorMapper
{
    /// <summary>
    /// Converts a System.ConsoleColor to its corresponding Spectre.Console color name.
    /// </summary>
    /// <param name="consoleColor">The ConsoleColor to convert.</param>
    /// <returns>The Spectre.Console color name (e.g., "red", "darkgreen", "grey").</returns>
    public static string ToSpectreColor(ConsoleColor consoleColor)
    {
        return consoleColor switch
        {
            ConsoleColor.Gray => "grey",
            ConsoleColor.DarkGray => "grey",
            ConsoleColor.Black => "black",
            ConsoleColor.Red => "red",
            ConsoleColor.DarkRed => "darkred",
            ConsoleColor.Green => "green",
            ConsoleColor.DarkGreen => "darkgreen",
            ConsoleColor.Yellow => "yellow",
            ConsoleColor.DarkYellow => "olive",
            ConsoleColor.Blue => "blue",
            ConsoleColor.DarkBlue => "darkblue",
            ConsoleColor.Magenta => "magenta",
            ConsoleColor.DarkMagenta => "darkmagenta",
            ConsoleColor.Cyan => "cyan",
            ConsoleColor.DarkCyan => "darkcyan",
            ConsoleColor.White => "white",
            _ => "default"
        };
    }
}

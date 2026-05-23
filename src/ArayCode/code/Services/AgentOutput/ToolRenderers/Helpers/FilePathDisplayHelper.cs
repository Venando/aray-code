using System.Collections.Generic;
using System.IO;

namespace ArayCode.Services;

/// <summary>
/// Shared logic for displaying file paths in tool renderers.
/// Truncates long paths by shortening the directory portion.
/// </summary>
internal static class FilePathDisplayHelper
{
    /// <summary>
    /// The maximum number of characters to display for the full path.
    /// </summary>
    private const int MaxLength = 25;

    /// <summary>
    /// Length of the ".." separator used when truncating.
    /// </summary>
    private const int EllipsisPrefixLength = 5;

    /// <summary>
    /// Formats a file path for display, truncating the directory portion
    /// if the full path exceeds <see cref="MaxLength"/> characters.
    /// Truncation preserves whole directory names — never shows partial folders.
    /// </summary>
    /// <param name="filePath">The full file path.</param>
    /// <returns>A path string suitable for display, possibly shortened.</returns>
    internal static string FormatDisplayPath(string filePath)
    {
        if (filePath.Length <= MaxLength)
            return filePath;

        string fileName = Path.GetFileName(filePath);
        string folder = Path.GetDirectoryName(filePath) ?? "";
        int availableFolderLength = MaxLength - fileName.Length - EllipsisPrefixLength;

        // Split into directory components and rebuild from the end,
        // keeping only complete directory names that fit.
        var parts = folder.Split(Path.DirectorySeparatorChar);
        var kept = new List<string>();
        int currentLength = 0;

        for (int i = parts.Length - 1; i >= 0; i--)
        {
            string part = parts[i];
            int addLength = part.Length + 1; // +1 for separator
            if (currentLength + addLength > availableFolderLength)
                break;
            kept.Insert(0, part);
            currentLength += addLength;
        }

        // Always keep at least the immediate parent directory
        if (kept.Count == 0 && parts.Length > 0)
            kept.Add(parts[^1]);

        string shortFolder = kept.Count == 0
            ? "..."
            : kept.Count < parts.Length
                ? "..." + Path.DirectorySeparatorChar + string.Join(Path.DirectorySeparatorChar.ToString(), kept)
                : string.Join(Path.DirectorySeparatorChar.ToString(), kept);

        return shortFolder + Path.DirectorySeparatorChar + fileName;
    }
}

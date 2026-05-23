namespace ArayCode.Services;

/// <summary>
/// Static class for computing line-based diffs between two texts using the LCS algorithm.
/// </summary>
public static class LineDiffEngine
{
    /// <summary>
    /// Computes a line-based diff between two arrays of lines.
    /// Returns a DiffResult containing ordered diff entries showing equal, removed, and added lines.
    /// </summary>
    public static DiffResult ComputeDiff(string[] oldLines, string[] newLines)
    {
        // Handle null/empty inputs
        oldLines ??= Array.Empty<string>();
        newLines ??= Array.Empty<string>();

        // Build LCS table
        int m = oldLines.Length;
        int n = newLines.Length;
        var dp = new int[m + 1, n + 1];

        for (int i = 1; i <= m; i++)
        {
            for (int j = 1; j <= n; j++)
            {
                if (oldLines[i - 1] == newLines[j - 1])
                    dp[i, j] = dp[i - 1, j - 1] + 1;
                else
                    dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
            }
        }

        // Backtrack to build diff
        var entries = new List<DiffEntry>();
        int x = m, y = n;
        int oldLineNum = m;
        int newLineNum = n;

        while (x > 0 || y > 0)
        {
            if (x > 0 && y > 0 && oldLines[x - 1] == newLines[y - 1])
            {
                entries.Add(new DiffEntry(DiffOperation.Equal, oldLines[x - 1], oldLineNum, newLineNum));
                x--;
                y--;
                oldLineNum--;
                newLineNum--;
            }
            else if (y > 0 && (x == 0 || dp[x, y - 1] >= dp[x - 1, y]))
            {
                entries.Add(new DiffEntry(DiffOperation.Add, newLines[y - 1], null, newLineNum));
                y--;
                newLineNum--;
            }
            else
            {
                entries.Add(new DiffEntry(DiffOperation.Remove, oldLines[x - 1], oldLineNum, null));
                x--;
                oldLineNum--;
            }
        }

        entries.Reverse();

        int additions = entries.Count(e => e.Operation == DiffOperation.Add);
        int removals = entries.Count(e => e.Operation == DiffOperation.Remove);
        int changes = Math.Min(additions, removals);

        return new DiffResult(entries, additions, removals, changes);
    }

    /// <summary>
    /// Computes a line-based diff between two texts.
    /// Convenience overload that splits strings into lines.
    /// </summary>
    public static DiffResult ComputeDiff(string oldText, string newText)
    {
        if (oldText == newText)
        {
            return new DiffResult(new List<DiffEntry>(), 0, 0, 0);
        }

        var oldLines = string.IsNullOrEmpty(oldText)
            ? Array.Empty<string>()
            : oldText.Split('\n');
        var newLines = string.IsNullOrEmpty(newText)
            ? Array.Empty<string>()
            : newText.Split('\n');

        return ComputeDiff(oldLines, newLines);
    }
}

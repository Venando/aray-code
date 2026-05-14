namespace OpenClawPTT.TTS;

/// <summary>
/// Temporary file creation and cleanup helpers for TTS providers.
/// Extracted to avoid duplicating temp path generation and cleanup
/// logic across providers (DRY).
/// </summary>
internal static class TempFileHelper
{
    /// <summary>Creates a unique temporary file path with the given prefix and extension.</summary>
    public static string CreateTempPath(string prefix, string extension)
    {
        return Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid()}{extension}");
    }

    /// <summary>Deletes a file if it exists, silently ignoring errors.</summary>
    public static void TryDelete(string? path)
    {
        if (path != null && File.Exists(path))
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }
}

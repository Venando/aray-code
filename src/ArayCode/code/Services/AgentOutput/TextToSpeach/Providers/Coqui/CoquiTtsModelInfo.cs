namespace ArayCode.TTS.Providers;

/// <summary>Info about an available Coqui TTS model.</summary>
public sealed class CoquiTtsModelInfo
{
    public string Name { get; }
    public string Description { get; }

    /// <summary>
    /// Disk size in bytes when the model is cached locally; <c>null</c> otherwise.
    /// Populated separately after cache discovery.
    /// </summary>
    public long? SizeBytes { get; set; }

    /// <summary>Human-readable size string (e.g. "1.2 GB"), or empty if unknown.</summary>
    public string FormattedSize => SizeBytes.HasValue ? FormatBytes(SizeBytes.Value) : "";

    public CoquiTtsModelInfo(string name, string description)
    {
        Name = name;
        Description = description;
    }

    /// <summary>
    /// Formats a byte count into a human-readable string (e.g. 1.2 GB, 345 MB, 12 KB).
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
            >= 1_024 => $"{bytes / 1_024.0:F0} KB",
            _ => $"{bytes} B"
        };
    }

    /// <summary>
    /// Derives a user-friendly description from the model path segments.
    /// E.g. "tts_models/en/ljspeech/vits" → "English · LJSpeech · VITS"
    /// </summary>
    public static CoquiTtsModelInfo FromModelName(string modelName)
    {
        var parts = modelName.Split('/');
        if (parts.Length < 3)
            return new CoquiTtsModelInfo(modelName, modelName);

        // tts_models / <lang> / <dataset> / <architecture>
        var lang = parts.Length > 1 ? parts[1] : "";
        var dataset = parts.Length > 2 ? parts[2] : "";
        var arch = parts.Length > 3 ? parts[3] : "";

        var desc = string.Join(" · ", new[] { lang, dataset, arch }
            .Where(s => !string.IsNullOrEmpty(s)));
        return new CoquiTtsModelInfo(modelName, desc);
    }
}

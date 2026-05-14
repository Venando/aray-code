namespace OpenClawPTT.TTS.Providers;

/// <summary>Info about an available Coqui TTS model.</summary>
public sealed class CoquiTtsModelInfo
{
    public string Name { get; }
    public string Description { get; }

    public CoquiTtsModelInfo(string name, string description)
    {
        Name = name;
        Description = description;
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

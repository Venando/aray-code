using System.Text;

namespace ArayCode.TTS;

/// <summary>
/// SSML (Speech Synthesis Markup Language) construction and text escaping helpers.
/// Extracted from <see cref="Providers.EdgeTtsProvider"/> to keep SSML concerns
/// separate from HTTP/API logic (SRP) and to make escaping reusable (DRY).
/// </summary>
internal static class SsmlHelper
{
    /// <summary>
    /// Builds a complete SSML document for Azure/Edge TTS.
    /// </summary>
    public static string BuildSsml(string text, string voiceName, string? lang = null)
    {
        lang ??= InferLanguage(voiceName);
        var escaped = EscapeSsml(text);
        return
            $"<speak version='1.0' xmlns='https://www.w3.org/2001/10/synthesis' xml:lang='{lang}'>" +
            $"<voice name='{voiceName}'>{escaped}</voice>" +
            $"</speak>";
    }

    /// <summary>
    /// Escapes special XML characters for SSML content.
    /// </summary>
    public static string EscapeSsml(string? text)
    {
        if (text == null) return string.Empty;

        // Use System.Security.SecurityElement.Escape for correct XML escaping
        var result = System.Security.SecurityElement.Escape(text);
        return result ?? string.Empty;
    }

    /// <summary>
    /// Infers the xml:lang attribute from a voice name like "en-US-AriaNeural".
    /// Falls back to "en-US" when no match is found.
    /// </summary>
    private static string InferLanguage(string voiceName)
    {
        // Voice names typically start with language-region (e.g. "en-US-", "de-DE-")
        if (!string.IsNullOrEmpty(voiceName))
        {
            var parts = voiceName.Split('-');
            if (parts.Length >= 2)
            {
                var lang = $"{parts[0]}-{parts[1]}";
                // Basic sanity: both parts should be 2-letter codes
                if (parts[0].Length == 2 && parts[1].Length == 2)
                    return lang;
            }
        }
        return "en-US";
    }
}

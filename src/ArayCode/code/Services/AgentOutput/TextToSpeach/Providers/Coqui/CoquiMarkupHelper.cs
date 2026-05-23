using System;

namespace ArayCode.TTS.Providers;

/// <summary>
/// Shared Spectre.Console markup escaping utilities for Coqui TTS files.
/// Deduplicates identical EscapeMarkup methods across
/// <see cref="CoquiTtsModelManager"/> and <see cref="CoquiEnvSetupPanel"/>.
/// </summary>
internal static class CoquiMarkupHelper
{
    /// <summary>
    /// Escapes Spectre.Console markup characters so raw process output
    /// won't break the rendering.
    /// </summary>
    public static string EscapeMarkup(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Replace("[", "[[").Replace("]", "]]");
    }

    /// <summary>
    /// Shortcut for <see cref="EscapeMarkup"/> — matches the longer method
    /// name used in <see cref="CoquiTtsModelManager"/>.
    /// </summary>
    public static string EscapeSpectreMarkup(string text) => EscapeMarkup(text);
}

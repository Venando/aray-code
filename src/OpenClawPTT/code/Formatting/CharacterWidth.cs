using System.Globalization;

namespace OpenClawPTT;

/// <summary>
/// Utility class for measuring the display width of text, accounting for
/// East Asian full-width characters that occupy two columns in a terminal.
/// </summary>
public static class CharacterWidth
{
    /// <summary>
    /// Returns the display width (1 or 2) of a single character.
    /// Half-width characters (ASCII, Latin-1, combining marks) return 1.
    /// Full-width characters (CJK, Hangul, fullwidth forms) return 2.
    /// </summary>
    public static int GetDisplayWidth(char c)
    {
        int code = c;

        // ASCII and Latin-1 control / narrow characters
        if (code < 0x1100) return 1;

        // Hangul Jamo
        if (code <= 0x115F) return 2;

        // Hangul Jamo Extended-A
        if (code >= 0xA960 && code <= 0xA97C) return 2;

        // Hangul Syllables
        if (code >= 0xAC00 && code <= 0xD7A3) return 2;

        // CJK Radicals Supplement / Kangxi Radicals
        if (code >= 0x2E80 && code <= 0x303E) return 2;

        // CJK Symbols and Punctuation / Hiragana / Katakana / Bopomofo
        // Hangul Compatibility Jamo / Kanbun / Bopomofo Extended / CJK Strokes
        // Enclosed CJK Letters and Months / CJK Compatibility
        if (code >= 0x3040 && code <= 0x33FF) return 2;

        // CJK Unified Ideographs Extension A
        if (code >= 0x3400 && code <= 0x4DBF) return 2;

        // CJK Unified Ideographs / Yi Script / Yi Radicals / Lisu
        if (code >= 0x4E00 && code <= 0xA4CF) return 2;

        // CJK Compatibility Ideographs
        if (code >= 0xF900 && code <= 0xFAFF) return 2;

        // Vertical Forms / CJK Compatibility Forms / Small Form Variants
        if (code >= 0xFE10 && code <= 0xFE6F) return 2;

        // Fullwidth Forms (FF01-FF60, FFE0-FFE6)
        if (code >= 0xFF01 && code <= 0xFF60) return 2;
        if (code >= 0xFFE0 && code <= 0xFFE6) return 2;

        // Supplementary: Kana Supplement / Kana Extended-A
        if (code >= 0x1B000 && code <= 0x1B12F) return 2;

        // Enclosed Ideographic Supplement
        if (code >= 0x1F200 && code <= 0x1F2FF) return 2;

        // CJK Unified Ideographs Extension B–F
        if (code >= 0x20000 && code <= 0x2FFFD) return 2;

        // CJK Unified Ideographs Extension G–H
        if (code >= 0x30000 && code <= 0x3FFFD) return 2;

        // Angle brackets (fullwidth form)
        if (code == 0x2329 || code == 0x232A) return 2;

        return 1;
    }

    /// <summary>
    /// Returns the total display width of a string, accounting for
    /// East Asian full-width characters.
    /// </summary>
    public static int GetDisplayWidth(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        int width = 0;
        foreach (char c in text)
        {
            width += GetDisplayWidth(c);
        }
        return width;
    }
}

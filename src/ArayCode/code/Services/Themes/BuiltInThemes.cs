namespace ArayCode.Services.Themes;

/// <summary>
/// Factory methods for in-code-only themes (no JSON file required).
/// Each theme is registered in <see cref="ThemeService"/> and appears
/// in <c>/theme</c> listings alongside file-based themes.
/// Properties not explicitly set inherit <see cref="ThemeConfig.Default"/> values.
/// </summary>
public static partial class BuiltInThemes
{
}

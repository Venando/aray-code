using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenClawPTT.Services.Themes;

/// <summary>
/// Event args for theme change notifications.
/// </summary>
public sealed class ThemeChangedEventArgs : EventArgs
{
    public ThemeConfig OldTheme { get; }
    public ThemeConfig NewTheme { get; }
    public string? ThemeFileName { get; }

    public ThemeChangedEventArgs(ThemeConfig oldTheme, ThemeConfig newTheme, string? themeFileName = null)
    {
        OldTheme = oldTheme;
        NewTheme = newTheme;
        ThemeFileName = themeFileName;
    }
}

/// <summary>
/// Manages theme loading, example file sync, and theme switching.
/// Themes are stored as JSON files in <c>{DataDir}/themes/</c>.
/// </summary>
public sealed class ThemeService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly AppConfig _appConfig;
    private ThemeConfig _currentTheme;

    /// <summary>Raised when the active theme is swapped via /theme command.</summary>
    public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    /// <summary>The currently active theme.</summary>
    public ThemeConfig CurrentTheme => _currentTheme;

    public ThemeService(AppConfig appConfig)
    {
        _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
        _currentTheme = ThemeConfig.Default;
    }

    /// <summary>
    /// Loads the theme specified in <see cref="AppConfig.ThemeFile"/>.
    /// If ThemeFile is empty or the file can't be loaded, uses <see cref="ThemeConfig.Default"/>.
    /// </summary>
    public ThemeConfig LoadTheme()
    {
        var themeFile = _appConfig.ThemeFile;
        if (string.IsNullOrWhiteSpace(themeFile))
        {
            _currentTheme = ThemeConfig.Default;
            return _currentTheme;
        }

        var themePath = Path.Combine(_appConfig.ThemesDir, themeFile);
        if (!File.Exists(themePath))
        {
            _currentTheme = ThemeConfig.Default;
            return _currentTheme;
        }

        try
        {
            var json = File.ReadAllText(themePath);
            var loaded = JsonSerializer.Deserialize<ThemeConfig>(json, JsonOpts);
            if (loaded == null)
            {
                _currentTheme = ThemeConfig.Default;
                return _currentTheme;
            }
            _currentTheme = loaded;
            return _currentTheme;
        }
        catch (JsonException)
        {
            // JSON parse error — fall back to defaults
            _currentTheme = ThemeConfig.Default;
            return _currentTheme;
        }
    }

    /// <summary>
    /// Ensures a <c>theme.example.json</c> file exists in the themes folder,
    /// containing the current default <see cref="ThemeConfig"/> values.
    /// If the file already exists but differs from defaults, it is overwritten.
    /// </summary>
    public void EnsureExampleFile()
    {
        var themesDir = _appConfig.ThemesDir;
        if (!Directory.Exists(themesDir))
            Directory.CreateDirectory(themesDir);

        var examplePath = Path.Combine(themesDir, "theme.example.json");
        var defaults = ThemeConfig.Default;
        var defaultJson = JsonSerializer.Serialize(defaults, JsonOpts);

        if (!File.Exists(examplePath))
        {
            File.WriteAllText(examplePath, defaultJson);
            return;
        }

        // Check if the existing example matches current defaults
        try
        {
            var existingJson = File.ReadAllText(examplePath);
            var existing = JsonSerializer.Deserialize<ThemeConfig>(existingJson, JsonOpts);
            if (existing != null && ThemesEqual(existing, defaults))
                return; // Already matches — no update needed
        }
        catch (JsonException)
        {
            // Corrupt example — will overwrite below
        }

        File.WriteAllText(examplePath, defaultJson);
    }

    /// <summary>
    /// Attempts to swap the active theme to the one identified by <paramref name="themeName"/>.
    /// The theme file is expected at <c>{ThemesDir}/{themeName}.json</c>.
    /// On success, raises <see cref="ThemeChanged"/>.
    /// </summary>
    /// <returns>True if the theme was loaded successfully; false if the file was not found or contained errors.</returns>
    public bool TrySwapTheme(string themeName)
    {
        if (string.IsNullOrWhiteSpace(themeName))
            return false;

        // If the name has no extension, append .json
        var fileName = themeName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? themeName
            : themeName + ".json";

        var themePath = Path.Combine(_appConfig.ThemesDir, fileName);
        if (!File.Exists(themePath))
            return false;

        try
        {
            var json = File.ReadAllText(themePath);
            var loaded = JsonSerializer.Deserialize<ThemeConfig>(json, JsonOpts);
            if (loaded == null)
                return false;

            var oldTheme = _currentTheme;
            _currentTheme = loaded;
            var fileNameOnly = Path.GetFileNameWithoutExtension(fileName);
            ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(oldTheme, loaded, fileNameOnly));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Lists all <c>.json</c> theme files found in the themes folder,
    /// excluding <c>theme.example.json</c>.
    /// </summary>
    public string[] GetAvailableThemes()
    {
        var themesDir = _appConfig.ThemesDir;
        if (!Directory.Exists(themesDir))
            return Array.Empty<string>();

        return Directory.GetFiles(themesDir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.Equals(name, "theme.example", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }

    /// <summary>
    /// Returns the display name of the currently active theme.
    /// Uses <see cref="ThemeConfig.Name"/> from the loaded theme,
    /// which reflects runtime swaps via <c>/theme</c>.
    /// Falls back to "Default" if the theme name is empty.
    /// </summary>
    public string GetCurrentThemeName()
    {
        var name = _currentTheme?.Name;
        return !string.IsNullOrWhiteSpace(name) ? name : "Default";
    }

    private static bool ThemesEqual(ThemeConfig a, ThemeConfig b)
    {
        return string.Equals(a.Name, b.Name, StringComparison.Ordinal) &&
               string.Equals(a.Author, b.Author, StringComparison.Ordinal) &&
               string.Equals(a.AccentColor, b.AccentColor, StringComparison.Ordinal) &&
               string.Equals(a.SecondaryColor, b.SecondaryColor, StringComparison.Ordinal) &&
               string.Equals(a.ForegroundColor, b.ForegroundColor, StringComparison.Ordinal) &&
               string.Equals(a.SuccessColor, b.SuccessColor, StringComparison.Ordinal) &&
               string.Equals(a.WarningColor, b.WarningColor, StringComparison.Ordinal) &&
               string.Equals(a.ErrorColor, b.ErrorColor, StringComparison.Ordinal) &&
               string.Equals(a.InfoColor, b.InfoColor, StringComparison.Ordinal) &&
               string.Equals(a.BorderColor, b.BorderColor, StringComparison.Ordinal) &&
               string.Equals(a.SelectionBackground, b.SelectionBackground, StringComparison.Ordinal) &&
               string.Equals(a.MutedColor, b.MutedColor, StringComparison.Ordinal);
    }
}

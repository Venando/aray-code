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
    private readonly Dictionary<string, ThemeConfig> _builtInThemes;

    /// <summary>Raised when the active theme is swapped via /theme command.</summary>
    public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    /// <summary>The currently active theme.</summary>
    public ThemeConfig CurrentTheme => _currentTheme;

    public ThemeService(AppConfig appConfig)
    {
        _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
        _currentTheme = ThemeConfig.Default;

        // Register in-code themes (no JSON files needed)
        _builtInThemes = new Dictionary<string, ThemeConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["drakula"] = BuiltInThemes.Drakula,
        };
    }

    /// <summary>
    /// Loads the theme specified in <see cref="AppConfig.ThemeFile"/>.
    /// If ThemeFile is empty or the file can't be loaded, uses <see cref="ThemeConfig.Default"/>.
    /// Updates <see cref="ThemeProvider.Current"/> so all theme-aware consumers pick up the change.
    /// </summary>
    public ThemeConfig LoadTheme()
    {
        var themeFile = _appConfig.ThemeFile;
        if (string.IsNullOrWhiteSpace(themeFile))
        {
            ApplyTheme(ThemeConfig.Default);
            return _currentTheme;
        }

        var themePath = Path.Combine(_appConfig.ThemesDir, themeFile);
        if (!File.Exists(themePath))
        {
            ApplyTheme(ThemeConfig.Default);
            return _currentTheme;
        }

        try
        {
            var json = File.ReadAllText(themePath);
            var loaded = JsonSerializer.Deserialize<ThemeConfig>(json, JsonOpts);
            if (loaded == null)
            {
                ApplyTheme(ThemeConfig.Default);
                return _currentTheme;
            }
            ApplyTheme(loaded);
            return _currentTheme;
        }
        catch (JsonException)
        {
            // JSON parse error — fall back to defaults
            ApplyTheme(ThemeConfig.Default);
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
    /// Checks built-in themes (no JSON file) first, then tries the themes folder.
    /// On success, raises <see cref="ThemeChanged"/>.
    /// </summary>
    /// <returns>True if the theme was loaded successfully; false if not found or had errors.</returns>
    public bool TrySwapTheme(string themeName)
    {
        if (string.IsNullOrWhiteSpace(themeName))
            return false;

        // Check built-in themes first (no JSON file needed)
        if (_builtInThemes.TryGetValue(themeName, out var builtIn))
        {
            var oldTheme = _currentTheme;
            ApplyTheme(builtIn);
            ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(oldTheme, _currentTheme, themeName));
            return true;
        }

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
            ApplyTheme(loaded);
            var fileNameOnly = Path.GetFileNameWithoutExtension(fileName);
            ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(oldTheme, _currentTheme, fileNameOnly));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Lists all available themes: built-in in-code themes + <c>.json</c>
    /// files from the themes folder (excluding <c>theme.example.json</c>).
    /// Built-in themes appear first, sorted alphabetically.
    /// </summary>
    public string[] GetAvailableThemes()
    {
        var themes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Built-in in-code themes
        foreach (var key in _builtInThemes.Keys)
            themes.Add(key);

        // File-based themes from the themes folder
        var themesDir = _appConfig.ThemesDir;
        if (Directory.Exists(themesDir))
        {
            foreach (var file in Directory.GetFiles(themesDir, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!string.Equals(name, "theme.example", StringComparison.OrdinalIgnoreCase))
                    themes.Add(name);
            }
        }

        return themes.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
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

    /// <summary>
    /// Applies the given ThemeConfig as the current theme both locally
    /// and via <see cref="ThemeProvider.Current"/>.
    /// </summary>
    private void ApplyTheme(ThemeConfig theme)
    {
        _currentTheme = theme;
        ThemeProvider.Current = theme;
    }

    /// <summary>
    /// Compares two ThemeConfig instances by serializing them to JSON.
    /// </summary>
    private static bool ThemesEqual(ThemeConfig a, ThemeConfig b)
    {
        var jsonA = JsonSerializer.Serialize(a, JsonOpts);
        var jsonB = JsonSerializer.Serialize(b, JsonOpts);
        return string.Equals(jsonA, jsonB, StringComparison.Ordinal);
    }
}

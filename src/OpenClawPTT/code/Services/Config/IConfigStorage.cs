using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenClawPTT.Services;

/// <summary>
/// Abstraction for config persistence, enabling testability.
/// </summary>
public interface IConfigStorage
{
    AppConfig? Load();
    void Save(AppConfig config);
}

/// <summary>
/// Default implementation that persists config to disk as JSON.
/// Preserves unrecognized fields and skips default-valued properties.
/// </summary>
public sealed class FileConfigStorage : IConfigStorage
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static readonly object _saveLock = new();

    public AppConfig? Load()
    {
        var cfg = new AppConfig();
        var path = ConfigPath(cfg);

        if (!File.Exists(path))
            return null;

        var json = File.ReadAllText(path);
        try
        {
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts);
            if (config != null)
            {
                using var doc = JsonDocument.Parse(json);

                // Backward compatibility: if VisualMode is missing or was None (0), disable via VisualFeedbackEnabled
                if (!doc.RootElement.TryGetProperty("VisualMode", out _) || config.VisualMode == 0)
                {
                    config.VisualFeedbackEnabled = false;
                    config.VisualMode = VisualMode.SolidDot;
                }

                // Set platform-specific default for EspeakNgPath if not configured
                if (string.IsNullOrEmpty(config.EspeakNgPath))
                {
                    config.EspeakNgPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? @"C:\Program Files\eSpeak NG"
                        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                            ? "/opt/homebrew/bin/espeak-ng"
                            : "/usr/bin/espeak-ng";
                }
            }
            return config;
        }
        catch (JsonException)
        {
            // Back up corrupted config before returning null so the user
            // can recover their settings. The timestamp suffix prevents
            // overwriting on repeated failures.
            try
            {
                var backupPath = path + $".corrupted.{DateTime.UtcNow:yyyyMMdd-HHmmss}";
                File.Copy(path, backupPath, overwrite: false);
            }
            catch
            {
                // Best-effort backup — if it fails (permissions, disk full),
                // don't let that prevent returning null.
            }

            return null;
        }
    }

    public void Save(AppConfig cfg)
    {
        var path = ConfigPath(cfg);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        lock (_saveLock)
        {
            // Load existing JSON to preserve unrecognized fields
            JsonNode? node = null;
            if (File.Exists(path))
            {
                var existingJson = File.ReadAllText(path);
                try
                {
                    node = JsonNode.Parse(existingJson);
                }
                catch (JsonException)
                {
                    node = null;
                }
            }

            node ??= new JsonObject();

            // Fresh defaults for comparison
            var defaults = new AppConfig();

            // Reflect over all public instance properties
            var props = typeof(AppConfig).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetMethod?.IsPublic == true);

            foreach (var prop in props)
            {
                var currentValue = prop.GetValue(cfg);
                var defaultValue = prop.GetValue(defaults);

                // Compare via JSON serialization to handle enums, strings, etc. uniformly
                var currentJson = JsonSerializer.SerializeToNode(currentValue, JsonOpts);
                var defaultJson = JsonSerializer.SerializeToNode(defaultValue, JsonOpts);

                if (JsonNode.DeepEquals(currentJson, defaultJson))
                {
                    // Remove if present so defaults don't clutter the file
                    if (node is JsonObject obj)
                        obj.Remove(prop.Name);
                }
                else
                {
                    // Update or add the property
                    node[prop.Name] = currentJson;
                }
            }

            // Atomic write: write to temp file, then rename over target.
            // If the process crashes mid-write, config.json remains intact.
            var json = node.ToJsonString(JsonOpts);
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, overwrite: true);
        }
    }

    private static string ConfigPath(AppConfig cfg)
    {
        var filename = "config.json";
#if DEBUG
        filename = "config.debug.json";
#endif
        return Path.Combine(cfg.DataDir, filename);
    }
}

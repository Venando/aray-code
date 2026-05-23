using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ArayCode.Services;

/// <summary>
/// Configuration data service: load, save, validate, and event publishing.
/// Pure data management — no UI or interactive wizard logic.
/// Interactive workflows are handled by <see cref="ConfigWizardOrchestrator"/>.
/// </summary>
public class ConfigurationService : IConfigurationService
{
    private readonly IConfigStorage _storage;
    private readonly object _saveLock = new();

    // Cached property info for diff computation (immutable per AppConfig type)
    private static readonly PropertyInfo[] AppConfigProperties = typeof(AppConfig)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.GetMethod?.IsPublic == true && !p.IsDefined(typeof(JsonIgnoreAttribute)))
        .ToArray();

    /// <inheritdoc />
    public event Action<ConfigChangedEventArgs>? ConfigValidating;

    /// <inheritdoc />
    public event Action<ConfigChangedEventArgs>? ConfigSaved;

    public ConfigurationService()
        : this(new FileConfigStorage())
    {
    }

    public ConfigurationService(IConfigStorage storage)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public AppConfig? Load() => _storage.Load();

    public void Save(AppConfig cfg)
    {
        lock (_saveLock)
        {
            // Load the old persisted config for diff comparison
            var oldCfg = _storage.Load();
            var changed = ComputeChangedProperties(oldCfg, cfg);
            _storage.Save(cfg);
            var args = new ConfigChangedEventArgs(changed, cfg);
            ConfigValidating?.Invoke(args);
            ConfigSaved?.Invoke(args);
        }
    }

    /// <summary>
    /// Computes which <see cref="AppConfig"/> properties differ between old and new.
    /// Uses JSON serialization for robust comparison (handles enums, nulls).
    /// Returns an empty set when both are null.
    /// </summary>
    internal static HashSet<string> ComputeChangedProperties(AppConfig? oldCfg, AppConfig? newCfg)
    {
        var changed = new HashSet<string>();

        if (ReferenceEquals(oldCfg, newCfg))
            return changed;

        foreach (var prop in AppConfigProperties)
        {
            var oldVal = oldCfg != null ? prop.GetValue(oldCfg) : null;
            var newVal = newCfg != null ? prop.GetValue(newCfg) : null;

            var oldJson = JsonSerializer.SerializeToNode(oldVal);
            var newJson = JsonSerializer.SerializeToNode(newVal);

            if (!JsonNode.DeepEquals(oldJson, newJson))
                changed.Add(prop.Name);
        }

        return changed;
    }

    public List<string> Validate(AppConfig? cfg)
    {
        var issues = new List<string>();

        if (cfg == null)
        {
            issues.Add("Configuration is null.");
            return issues;
        }

        if (string.IsNullOrWhiteSpace(cfg.GatewayUrl))
            issues.Add("Gateway URL is required.");

        if (!Uri.TryCreate(cfg.GatewayUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "ws" && uri.Scheme != "wss"))
            issues.Add("Gateway URL must start with ws:// or wss://");

        if (string.IsNullOrWhiteSpace(cfg.AuthToken)
            && string.IsNullOrWhiteSpace(cfg.DeviceToken))
            issues.Add("Auth token or device token is required.");

        if (cfg.SampleRate is < 8000 or > 48000)
            issues.Add("Sample rate must be between 8000 and 48000.");

        if (cfg.ReconnectDelaySeconds <= 0)
            issues.Add("Reconnect delay must be positive.");

        if (!Enum.IsDefined(typeof(VisualMode), cfg.VisualMode))
            issues.Add($"VisualMode must be a defined value (current: {cfg.VisualMode}).");

        return issues;
    }
}

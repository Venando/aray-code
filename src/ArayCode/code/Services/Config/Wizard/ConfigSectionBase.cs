using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArayCode.Services;

namespace ArayCode.ConfigWizard;

/// <summary>
/// Base class for modular config sections with generic <see cref="ConfigSetupItem"/> support.
/// Subclasses populate <see cref="_configItems"/> in their constructors or dynamically before
/// calling <see cref="RunConfigItemsAsync"/>.
/// </summary>
public abstract class ConfigSectionBase : IConfigSectionWizard
{
    public abstract string Name { get; }
    public abstract string Description { get; }

    /// <summary>
    /// Config items to run unconditionally. Subclasses populate this eagerly or dynamically.
    /// </summary>
    protected readonly List<ConfigSetupItem> _configItems = new();

    /// <summary>
    /// Config items organized by tag (e.g. provider name), for conditional/selective execution.
    /// Subclasses populate via <see cref="AddConfigItem"/> in their constructors or dynamically.
    /// </summary>
    protected readonly Dictionary<string, List<ConfigSetupItem>> _configItemsByTag = new();

    /// <summary>Adds an item under a named tag for selective execution.</summary>
    protected void AddConfigItem(string tag, ConfigSetupItem item)
    {
        if (!_configItemsByTag.TryGetValue(tag, out var list))
        {
            list = new List<ConfigSetupItem>();
            _configItemsByTag[tag] = list;
        }
        list.Add(item);
    }

    /// <summary>
    /// Runs all registered <see cref="_configItems"/> from <paramref name="startIndex"/> onward in sequence.
    /// Automatically populates <paramref name="result"/>.<see cref="ConfigSectionResult.Settings"/>
    /// with the display value of each item after prompting.
    /// Returns true if any item reported a change.
    /// </summary>
    protected async Task<bool> RunConfigItemsAsync(
        IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct,
        ConfigSectionResult result, int startIndex = 0)
    {
        bool changed = false;
        for (int i = startIndex; i < _configItems.Count; i++)
        {
            var item = _configItems[i];
            if (await item.RunAsync(host, config, isInitialSetup, ct))
                changed = true;
            result.Settings.Add(new ConfigSectionResult.SettingRecord(
                item.Title, item.GetDisplayValue(config)));
        }
        return changed;
    }

    /// <summary>
    /// Runs all items registered under the given <paramref name="tag"/>.
    /// Automatically populates <paramref name="result"/>.<see cref="ConfigSectionResult.Settings"/>
    /// with the display value of each item after prompting.
    /// Returns true if any item reported a change.
    /// </summary>
    protected async Task<bool> RunConfigItemsByTagAsync(
        string tag, IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct,
        ConfigSectionResult result)
    {
        if (!_configItemsByTag.TryGetValue(tag, out var items))
            return false;

        bool changed = false;
        foreach (var item in items)
        {
            if (await item.RunAsync(host, config, isInitialSetup, ct))
                changed = true;
            result.Settings.Add(new ConfigSectionResult.SettingRecord(
                item.Title, item.GetDisplayValue(config)));
        }
        return changed;
    }

    public abstract Task<ConfigSectionResult> RunAsync(
        IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct);
}

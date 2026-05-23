using System;
using System.Collections.Generic;
using System.Linq;

namespace ArayCode.Services;

/// <summary>
/// Event args for <see cref="IConfigurationService.ConfigSaved"/>.
/// Carries the new config and the set of property names that actually changed.
/// Subscribers should check <see cref="ChangedPropertyNames"/> before acting
/// to avoid unnecessary work on unrelated config changes.
/// </summary>
public sealed class ConfigChangedEventArgs : EventArgs
{
    /// <summary>The fully resolved new configuration.</summary>
    public AppConfig NewConfig { get; }

    /// <summary>
    /// Property names (from <see cref="AppConfig"/>) whose values changed
    /// in this save. Empty set means only unrecognized/transient fields changed.
    /// </summary>
    public IReadOnlySet<string> ChangedPropertyNames { get; }

    /// <summary>
    /// True if the given <paramref name="propertyName"/> is in <see cref="ChangedPropertyNames"/>.
    /// Case-sensitive match on C# property names.
    /// </summary>
    public bool IsChanged(string propertyName) => ChangedPropertyNames.Contains(propertyName);

    /// <summary>True when any of the given <paramref name="propertyNames"/> changed.</summary>
    public bool AnyChanged(params string[] propertyNames) =>
        propertyNames.Any(p => ChangedPropertyNames.Contains(p));

    public ConfigChangedEventArgs(IReadOnlySet<string> changedPropertyNames, AppConfig newConfig)
    {
        ChangedPropertyNames = changedPropertyNames ?? throw new ArgumentNullException(nameof(changedPropertyNames));
        NewConfig = newConfig ?? throw new ArgumentNullException(nameof(newConfig));
    }
}

using System;
using System.Collections.Generic;

namespace OpenClawPTT.Services;

/// <summary>
/// Configuration data service: load, save, validate, and event publishing.
/// Pure data management — no UI or interactive logic.
/// Interactive workflows (setup wizard, reconfiguration) are handled by
/// <see cref="IConfigWizardOrchestrator"/>.
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// Raised after validation passes but before the config is persisted.
    /// Subscribers can use this to prepare for config changes (e.g. close
    /// resources that depend on the old config values).
    /// Fires before <see cref="ConfigSaved"/>.
    /// </summary>
    event Action<ConfigChangedEventArgs>? ConfigValidating;

    /// <summary>
    /// Raised whenever the configuration is successfully saved.
    /// Carries <see cref="ConfigChangedEventArgs"/> with the new config and
    /// the set of property names that actually changed.
    /// Subscribers should check <c>e.IsChanged(...)</c> before acting.
    /// </summary>
    event Action<ConfigChangedEventArgs>? ConfigSaved;

    AppConfig? Load();
    void Save(AppConfig cfg);
    List<string> Validate(AppConfig cfg);
}

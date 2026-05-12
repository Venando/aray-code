using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OpenClawPTT.Services;

public interface IConfigurationService
{
    /// <summary>
    /// Raised whenever the configuration is successfully saved.
    /// Subscribers can use this to react to config changes (e.g. re-probe LLM endpoint).
    /// </summary>
    event Action<AppConfig>? ConfigSaved;

    Task<AppConfig> LoadOrSetupAsync(IStreamShellHost shellHost, bool forceReconfigure = false, CancellationToken ct = default);
    Task<AppConfig> ReconfigureAsync(IStreamShellHost shellHost, AppConfig existing, CancellationToken ct);
    AppConfig? Load();
    void Save(AppConfig cfg);
    List<string> Validate(AppConfig cfg);
}

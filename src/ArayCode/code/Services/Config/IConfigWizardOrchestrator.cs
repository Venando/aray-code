using System.Threading;
using System.Threading.Tasks;
using StreamShell;

namespace ArayCode.Services;

/// <summary>
/// Orchestrates interactive configuration workflows (initial setup and reconfiguration).
/// Separated from <see cref="IConfigurationService"/> to obey SRP — configuration data
/// management (load/save/validate/events) lives in <see cref="IConfigurationService"/>,
/// while interactive UI flows live here.
/// </summary>
public interface IConfigWizardOrchestrator
{
    /// <summary>
    /// Loads configuration from storage. If none exists or forced reconfiguration,
    /// runs the interactive setup wizard via <paramref name="shellHost"/>.
    /// </summary>
    Task<AppConfig> LoadOrSetupAsync(IStreamShellHost shellHost, bool forceReconfigure = false, CancellationToken ct = default);

    /// <summary>
    /// Runs the reconfiguration wizard, allowing the user to modify sections of
    /// an existing configuration interactively. Saves the result on completion.
    /// </summary>
    Task<AppConfig> ReconfigureAsync(IStreamShellHost shellHost, AppConfig existing, CancellationToken ct);
}

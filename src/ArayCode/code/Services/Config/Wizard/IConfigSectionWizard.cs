using System.Threading;
using System.Threading.Tasks;
using ArayCode.Services;

namespace ArayCode.ConfigWizard;

/// <summary>
/// A self-contained configuration section that can be run during initial setup
/// or independently during reconfiguration.
/// </summary>
public interface IConfigSectionWizard
{
    /// <summary>Display name shown in the reconfiguration menu.</summary>
    string Name { get; }

    /// <summary>Short description of what this section configures.</summary>
    string Description { get; }

    /// <summary>
    /// Runs this configuration section.
    /// </summary>
    /// <param name="host">StreamShell host for prompting.</param>
    /// <param name="config">The config to mutate.</param>
    /// <param name="isInitialSetup">True during first-time setup (no "Back" option on first prompt).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if any setting was changed.</returns>
    Task<ConfigSectionResult> RunAsync(IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct);
}

public class ConfigSectionResult
{
    public readonly List<SettingRecord> Settings = new List<SettingRecord>();

    public bool IsChanged { get; set;}
    
    public record SettingRecord(string Name, string DisplayValue);
}


using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using StreamShell;

namespace OpenClawPTT.ConfigWizard;

/// <summary>
/// Modular configuration wizard that runs config sections sequentially during initial setup,
/// or presents a menu during reconfiguration.
/// Uses StreamShell PromptSelection for all choice-based prompts.
/// </summary>
public sealed class ModularConfigurationWizard
{
    /// <summary>Set to true while the wizard is active so other input handlers can skip processing.</summary>
    public static volatile bool IsActive;

    private readonly IReadOnlyList<IConfigSectionWizard> _sections;

    public ModularConfigurationWizard()
    {
        _sections = new List<IConfigSectionWizard>
        {
            new HarnessConfigSection(),
            new SttConfigSection(),
            new TtsConfigSection(),
            new InputDisplayConfigSection(),
            new VisualFeedbackConfigSection(),
        };
    }

    public ModularConfigurationWizard(IEnumerable<IConfigSectionWizard> sections)
    {
        _sections = sections.ToList();
    }

    // ── Initial setup ────────────────────────────────────────────────

    /// <summary>
    /// Runs all sections sequentially for first-time setup.
    /// </summary>
    public async Task<AppConfig> RunInitialSetupAsync(IStreamShellHost host, CancellationToken ct = default)
    {
        IsActive = true;
        try
        {
            var config = new AppConfig();

            host.AddMessage("[cyan2]═══════════════════════════════════════════[/]");
            host.AddMessage("[cyan2]  Welcome! Let's configure OpenClaw PTT.  [/]");
            host.AddMessage("[cyan2]═══════════════════════════════════════════[/]");

            foreach (var section in _sections)
            {
                ct.ThrowIfCancellationRequested();
                host.AddMessage("");
                host.AddMessage($"[bold cyan]▶ {section.Name}[/] [grey]- {section.Description}[/]");
                await section.RunAsync(host, config, isInitialSetup: true, ct);
            }

            host.AddMessage("");
            host.AddMessage("[green]  ✓ Setup complete![/]");
            return config;
        }
        finally
        {
            IsActive = false;
        }
    }

    // ── Reconfiguration ──────────────────────────────────────────────

    /// <summary>
    /// Presents a menu to pick which section to reconfigure, then runs it.
    /// Returns the updated config (may be the same reference if nothing changed).
    /// </summary>
    public async Task<AppConfig> RunReconfigureAsync(IStreamShellHost host, AppConfig existing, CancellationToken ct = default)
    {
        IsActive = true;
        try
        {
            var config = Clone(existing);
            bool anyChanged = false;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                // Build menu variants
                var variants = new List<IVariant>
                {
                    new ConfigVariant("[red]Cancel[/]", "__cancel__")
                };
                foreach (var section in _sections)
                {
                    variants.Add(new ConfigVariant($"{section.Name} [grey]- {section.Description}[/]", section.Name));
                }

                host.AddMessage("");
                host.AddMessage("[bold cyan]Configuration[/]");

                string choice;
                const int maxAttempts = 3;
                int attempts = 0;
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var result = await host.PromptSelection("Select section to configure:", variants.ToArray());
                        if (result is { Length: > 0 })
                        {
                            choice = ((ConfigVariant)result[0]).Value;
                            break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // ignored — re-prompt
                    }

                    attempts++;
                    if (attempts >= maxAttempts)
                    {
                        host.AddMessage("[yellow]  Too many cancellations — exiting reconfiguration.[/]");
                        return anyChanged ? config : existing;
                    }
                }

                if (choice == "__cancel__")
                {
                    host.AddMessage("[grey]  Reconfiguration cancelled.[/]");
                    break;
                }

                var selectedSection = _sections.FirstOrDefault(s => s.Name == choice);
                if (selectedSection == null)
                    continue;

                host.AddMessage("");
                host.AddMessage($"[bold cyan]▶ {selectedSection.Name}[/]");
                var changed = await selectedSection.RunAsync(host, config, isInitialSetup: false, ct);
                if (changed)
                {
                    anyChanged = true;
                    host.AddMessage($"[green]  ✓ {selectedSection.Name} updated.[/]");
                }
                else
                {
                    host.AddMessage($"[grey]  → {selectedSection.Name} unchanged.[/]");
                }
            }

            return anyChanged ? config : existing;
        }
        finally
        {
            IsActive = false;
        }
    }

    // ── Clone helper ─────────────────────────────────────────────────

    private static AppConfig Clone(AppConfig source)
    {
        var options = new JsonSerializerOptions();
        var json = JsonSerializer.Serialize(source, options);
        var clone = JsonSerializer.Deserialize<AppConfig>(json, options);
        if (clone == null)
            throw new InvalidOperationException("Failed to clone AppConfig via JSON round-trip.");
        clone.CustomDataDir = source.CustomDataDir;
        return clone;
    }
}

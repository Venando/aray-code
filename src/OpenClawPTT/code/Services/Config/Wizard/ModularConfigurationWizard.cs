using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using Spectre.Console;
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
    [Obsolete("Use WizardState.IsActive instead")]
    public static bool IsActive => WizardState.IsActive;

    private readonly IReadOnlyList<IConfigSectionWizard> _sections;

    public ModularConfigurationWizard()
    {
        _sections = new List<IConfigSectionWizard>
        {
            new HarnessConfigSection(),
            new SttConfigSection(),
            new TtsConfigSection(),
            new DirectLlmConfigSection(),
            new InputDisplayConfigSection(),
            new VisualFeedbackConfigSection(),
        };
    }

    public ModularConfigurationWizard(IEnumerable<IConfigSectionWizard> sections)
    {
        _sections = sections.ToList();
    }

    // ── Initial setup ────────────────────────────────────────────────

    private const string RedoSentinel = "__redo__";

    /// <summary>
    /// Runs all sections sequentially for first-time setup.
    /// After each section, displays the settings summary and offers Continue / Redo entire section.
    /// Choosing Redo clears the screen and runs the section again from scratch.
    /// </summary>
    public async Task<AppConfig> RunInitialSetupAsync(IStreamShellHost host, CancellationToken ct = default)
    {
        WizardState.Enter();
        try
        {
            var config = new AppConfig();

            foreach (var section in _sections)
            {
                bool redo;
                do
                {
                    redo = false;
                    ct.ThrowIfCancellationRequested();

                    for (int i = 0; i < ConsoleMetrics.GetWindowHeight(); i++)
                        host.AddMessage("");

                    host.AddMessage($"[bold cyan2]──────────● [underline]{section.Name}[/]      [/]");
                    host.AddMessage($"[grey]{ section.Description} [/]");
                    var sectionResult = await section.RunAsync(host, config, isInitialSetup: true, ct);

                    // Skip review if no settings to show
                    if (sectionResult.Settings.Count == 0)
                        continue;

                    // ── Display settings summary ──
                    host.AddMessage("");
                    host.AddMessage("[bold]  Settings:[/]");
                    foreach (var setting in sectionResult.Settings)
                    {
                        var escapedValue = Markup.Escape(setting.DisplayValue);
                        host.AddMessage($"    [grey]{Markup.Escape(setting.Name)}[/] → [cyan]{escapedValue}[/]");
                    }

                    // ── Continue or Redo the whole section ──
                    host.AddMessage("");
                    var options = new (string Name, string Value)[]
                    {
                        ("Continue", "continue"),
                        ("Redo this section", RedoSentinel),
                    };

                    var choice = await PromptSelectionHelper.PromptStringAsync(
                        host, "Continue to next section or redo this one?", options,
                        defaultValue: "continue", allowCancel: false, cancellationToken: ct);

                    redo = choice == RedoSentinel;

                } while (redo);
            }

            host.AddMessage("");
            host.AddMessage("[green]  ✓ Setup complete![/]");
            return config;
        }
        finally
        {
            WizardState.Leave();
        }
    }

    // ── Reconfiguration ──────────────────────────────────────────────

    /// <summary>
    /// Presents a menu to pick which section to reconfigure, then runs it.
    /// Returns the updated config (may be the same reference if nothing changed).
    /// </summary>
    public async Task<AppConfig> RunReconfigureAsync(IStreamShellHost host, AppConfig existing, CancellationToken ct = default)
    {
        WizardState.Enter();
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
                    new ConfigVariant("[red]Cancel[/]", PromptSelectionHelper.CancelSentinel)
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
                        if (result is { Length: > 0 } && result[0] is ConfigVariant cv)
                        {
                            choice = cv.Value;
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

                if (choice == PromptSelectionHelper.CancelSentinel)
                {
                    host.AddMessage("[grey]  Reconfiguration cancelled.[/]");
                    break;
                }

                var selectedSection = _sections.FirstOrDefault(s => s.Name == choice);
                if (selectedSection == null)
                    continue;

                host.AddMessage("");
                host.AddMessage($"[bold cyan]▶ {selectedSection.Name}[/]");
                var sectionResult = await selectedSection.RunAsync(host, config, isInitialSetup: false, ct);
                if (sectionResult.IsChanged)
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
            WizardState.Leave();
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
        clone.ReservedRightMargin = source.ReservedRightMargin;
        return clone;
    }
}

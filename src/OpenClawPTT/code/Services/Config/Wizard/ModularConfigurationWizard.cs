using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using OpenClawPTT.TTS;
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

            // ── Show current configuration summary before menu ──
            ShowCurrentConfigSummary(host, config);

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

    // ── Status summary ──────────────────────────────────────────────

    /// <summary>
    /// Displays a concise summary of the current active configuration.
    /// Shows the active providers/models for each service section.
    /// </summary>
    private static void ShowCurrentConfigSummary(IStreamShellHost host, AppConfig config)
    {
        host.AddMessage("");
        host.AddMessage("[bold]Current configuration:[/]");

        // Harness
        var gwUrl = string.IsNullOrWhiteSpace(config.GatewayUrl)
            ? "(not set)"
            : config.GatewayUrl;
        host.AddMessage($"  [cyan]●[/] [bold]Harness:[/] OpenClaw [grey]({Markup.Escape(gwUrl)})[/]");

        // STT
        var sttProvider = config.SttProvider ?? "(not set)";
        var sttModel = sttProvider switch
        {
            "groq" => config.GroqModel ?? "whisper-large-v3-turbo",
            "openai" => config.OpenAiModel ?? "whisper-1",
            "whisper-cpp" => config.WhisperCppModel ?? "(default)",
            "faster-whisper" => config.FasterWhisperModel ?? "(default)",
            _ => "",
        };
        if (!string.IsNullOrEmpty(sttModel))
            host.AddMessage($"  [cyan]●[/] [bold]STT:[/] {Markup.Escape(sttProvider)} [grey](model: {Markup.Escape(sttModel ?? "")})[/]");
        else
            host.AddMessage($"  [cyan]●[/] [bold]STT:[/] {Markup.Escape(sttProvider)}");

        // TTS
        var ttsProvider = config.TtsProvider.ToString();
        var ttsVoice = string.IsNullOrWhiteSpace(config.TtsVoice) ? "(default)" : config.TtsVoice;
        var ttsMode = config.TtsOutputMode ?? "siso";
        host.AddMessage($"  [cyan]●[/] [bold]TTS:[/] {Markup.Escape(ttsProvider)} [grey](voice: {Markup.Escape(ttsVoice)}, mode: {Markup.Escape(ttsMode)})[/]");

        // Direct LLM
        var llmConfigured = !string.IsNullOrWhiteSpace(config.DirectLlmUrl)
                          && !string.IsNullOrWhiteSpace(config.DirectLlmModelName);
        if (llmConfigured)
        {
            host.AddMessage($"  [cyan]●[/] [bold]Direct LLM:[/] {Markup.Escape(config.DirectLlmModelName ?? "")} [grey]@ {Markup.Escape(config.DirectLlmUrl ?? "")}[/]");
        }
        else
        {
            host.AddMessage($"  [cyan]●[/] [bold]Direct LLM:[/] [grey](not configured)[/]");
        }

        // Input & Display
        var hotkey = string.IsNullOrWhiteSpace(config.HotkeyCombination) ? "(not set)" : config.HotkeyCombination;
        var hold = config.HoldToTalk ? "hold" : "toggle";
        var displayMode = config.ReplyDisplayMode.ToString();
        host.AddMessage($"  [cyan]●[/] [bold]Input & Display:[/] Hotkey: {Markup.Escape(hotkey)} [grey]|[/] Mode: {Markup.Escape(hold)} [grey]|[/] Reply: {Markup.Escape(displayMode)}");

        // Visual Feedback
        var vfEnabled = config.VisualFeedbackEnabled ? "enabled" : "disabled";
        var vfStyle = config.VisualMode.ToString();
        host.AddMessage($"  [cyan]●[/] [bold]Visual Feedback:[/] {Markup.Escape(vfEnabled)} [grey]({Markup.Escape(vfStyle)})[/]");

        host.AddMessage("");
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

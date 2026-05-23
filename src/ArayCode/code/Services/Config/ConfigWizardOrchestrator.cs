using System;
using System.Threading;
using System.Threading.Tasks;
using ArayCode.ConfigWizard;
using ArayCode.Services.Themes;
using Spectre.Console;
using StreamShell;

namespace ArayCode.Services;

/// <summary>
/// Orchestrates interactive configuration workflows: initial setup and reconfiguration.
/// Delegates data persistence to <see cref="IConfigurationService"/> and UI prompting
/// to <see cref="ModularConfigurationWizard"/> + StreamShell.
///
/// SRP: This class owns the *interactive* config flow (UI logic, user choices, looping).
/// It has no knowledge of how config data is stored or validated — those are
/// <see cref="IConfigurationService"/>'s job.
/// </summary>
public sealed class ConfigWizardOrchestrator : IConfigWizardOrchestrator
{
    private readonly IConfigurationService _configService;
    private readonly ModularConfigurationWizard _wizard;

    public ConfigWizardOrchestrator(IConfigurationService configService)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _wizard = new ModularConfigurationWizard();
    }

    /// <inheritdoc />
    public async Task<AppConfig> LoadOrSetupAsync(IStreamShellHost shellHost, bool forceReconfigure = false, CancellationToken ct = default)
    {
        var cfg = _configService.Load();

        if (cfg is null)
        {
            shellHost.AddMessage($"[{ThemeProvider.Current.Tools.Panel.SectionHeader}]────● No configuration found — starting first-time setup.      [/]");
            await shellHost.PromptSelection("Continue?", [new Variant("Yes")]);
            cfg = await _wizard.RunInitialSetupAsync(shellHost, ct);
            _configService.Save(cfg);
            shellHost.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Success}]Configuration saved.[/]");
            return cfg;
        }

        var issues = _configService.Validate(cfg);
        bool needsSetup = issues.Count > 0 || forceReconfigure;

        if (issues.Count > 0)
        {
            shellHost.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Highlight}]Configuration issues detected:[/]");
            foreach (var i in issues)
                shellHost.AddMessage($"  [{ThemeProvider.Current.Tools.General.Muted}]• {Markup.Escape(i)}[/]");
        }

        if (needsSetup)
        {
            if (forceReconfigure)
                shellHost.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Highlight}]Starting setup wizard...[/]");
            else
                shellHost.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Highlight}]Starting setup wizard to fix missing/invalid fields...[/]");

            cfg = await _wizard.RunInitialSetupAsync(shellHost, ct);
            _configService.Save(cfg);
        }

        return cfg;
    }

    /// <inheritdoc />
    public async Task<AppConfig> ReconfigureAsync(IStreamShellHost shellHost, AppConfig existing, CancellationToken ct)
    {
        shellHost.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Highlight}]Starting reconfiguration wizard...[/]");

        AppConfig newCfg;
        try
        {
            newCfg = await _wizard.RunReconfigureAsync(shellHost, existing, ct);
        }
        catch (OperationCanceledException)
        {
            return existing;
        }

        // Validate the result — the wizard may skip sections, leaving issues.
        var issues = _configService.Validate(newCfg);
        if (issues.Count > 0)
        {
            shellHost.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]Configuration issues found after reconfiguration:[/]");
            foreach (var i in issues)
                shellHost.AddMessage($"  [{ThemeProvider.Current.Tools.General.Muted}]\u2022 {Markup.Escape(i)}[/]");
            shellHost.AddMessage($"[{ThemeProvider.Current.Tools.General.Muted}](the wizard may have skipped some sections)[/]");

            // Offer to re-enter the wizard to fix remaining issues.
            // Uses the existing PromptSelectionHelper from ArayCode.ConfigWizard.
            var retry = await PromptSelectionHelper.PromptStringAsync(
                shellHost, "Re-enter wizard to fix issues?",
                new (string Name, string Value)[]
                {
                    ("Yes, re-enter wizard", "yes"),
                    ("No, save as-is", "no"),
                },
                defaultValue: "yes", allowCancel: false, cancellationToken: ct);

            if (retry == "yes")
            {
                return await ReconfigureAsync(shellHost, newCfg, ct);
            }
        }

        _configService.Save(newCfg);
        return newCfg;
    }

    /// <summary>
    /// Small wrapper so we can call PromptSelection without exposing IVariant.
    /// </summary>
    private sealed record Variant(string Name) : IVariant;
}

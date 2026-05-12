using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using StreamShell;

namespace OpenClawPTT.ConfigWizard;

/// <summary>Configures harness selection and gateway connection settings.</summary>
public sealed class HarnessConfigSection : ConfigSectionBase
{
    public override string Name => "Harness";
    public override string Description => "Harness type and gateway connection";

    private static readonly (string Name, string Value)[] HarnessOptions =
    {
        ("OpenClaw", "openclaw"),
        ("Nanobot (not supported)", "nanobot"),
    };

    public HarnessConfigSection()
    {
        _configItems.AddRange(new[]
        {
            ConfigSetupItem.ForString(
                title: "Gateway URL",
                fieldName: nameof(AppConfig.GatewayUrl),
                validator: v => Uri.TryCreate(v, UriKind.Absolute, out var uri)
                    && (uri.Scheme == "ws" || uri.Scheme == "wss"),
                validationHint: "Expected ws:// or wss:// URL",
                isEmptyToDefault: true),
            ConfigSetupItem.ForString(
                title: "Auth token (OPENCLAW_GATEWAY_TOKEN env)",
                fieldName: nameof(AppConfig.AuthToken),
                isSecret: true,
                isEmptyToDefault: false,
                validator: (value) => !string.IsNullOrWhiteSpace(value)),
        });
    }

    public override async Task<ConfigSectionResult> RunAsync(
        IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct)
    {
        var result = new ConfigSectionResult();
        bool changed = false;

        // ── Harness type ──
        var harness = await SelectHarnessAsync(host, result, isInitialSetup, ct);
        if (harness == null)
            return result;

        ConfigSelectionHelper.PrintSubSection(host, harness, "harness setup");

        // ── Seed auth token from env var if not already set ──
        if (config.AuthToken == null)
            config.AuthToken = Environment.GetEnvironmentVariable("OPENCLAW_GATEWAY_TOKEN");

        // ── Loop over generic config items ──
        if (await RunConfigItemsAsync(host, config, isInitialSetup, ct, result))
            changed = true;

        // ── TLS fingerprint (only for wss://) ──
        if (Uri.TryCreate(config.GatewayUrl, UriKind.Absolute, out var uri)
            && uri.Scheme == "wss")
        {
            var tlsFingerprint = await PromptTextHelper.PromptAsync(host, "TLS cert fingerprint (optional, for wss:// pinning)",
                config.TlsFingerprint ?? "",
                _ => true,
                null,
                ct, isEmptyToDefault: true);
            if (tlsFingerprint != null)
            {
                var newValue = string.IsNullOrWhiteSpace(tlsFingerprint) ? null : tlsFingerprint;
                if (newValue != config.TlsFingerprint)
                {
                    config.TlsFingerprint = newValue;
                    changed = true;
                }
            }
        }

        // ── Populate settings summary ──
        result.Settings.Add(new ConfigSectionResult.SettingRecord("Harness Type", harness));

        result.IsChanged = changed;
        return result;
    }

    /// <summary>Prompts the user for a harness type. Returns null if the section should abort.</summary>
    private async Task<string?> SelectHarnessAsync(
        IStreamShellHost host, ConfigSectionResult result, bool isInitialSetup, CancellationToken ct)
    {
        string? harness = null;

        while (harness == null)
        {
            var harnessResult = await PromptSelectionHelper.PromptStringAsync(host,
                "Choose harness:", HarnessOptions, cancellationToken: ct);

            if (harnessResult == null)
            {
                result.IsChanged = false;
                return null;
            }
            
            harness = harnessResult;

            // For now only OpenClaw is supported; Nanobot is a placeholder
            if (harness == "nanobot")
            {
                host.AddMessage("[dim]Nanobot harness is not yet supported yet[/]");
                harness = null;
            }
        }

        return harness;
    }
}

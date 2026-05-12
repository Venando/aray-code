using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using StreamShell;

namespace OpenClawPTT.ConfigWizard;

/// <summary>Configures harness selection and gateway connection settings.</summary>
public sealed class HarnessConfigSection : IConfigSectionWizard
{
    public string Name => "Harness";
    public string Description => "Harness type and gateway connection";

    private readonly ConfigSetupItem[] _configItems;

    public HarnessConfigSection()
    {
        _configItems = new ConfigSetupItem[]
        {
            ConfigSetupItem.ForString(
                title: "Gateway URL",
                fieldName: nameof(AppConfig.GatewayUrl),
                validator: v => Uri.TryCreate(v, UriKind.Absolute, out var uri)
                    && (uri.Scheme == "ws" || uri.Scheme == "wss"),
                validationHint: "Expected ws:// or wss:// URL"),
            ConfigSetupItem.ForString(
                title: "Auth token (OPENCLAW_GATEWAY_TOKEN env)",
                fieldName: nameof(AppConfig.AuthToken),
                isSecret: true,
                isEmptyToDefault: false),
        };
    }

    public async Task<ConfigSectionResult> RunAsync(
        IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct)
    {
        var result = new ConfigSectionResult();
        bool changed = false;

        // ── Harness type ──
        var harnessOptions = new (string Name, string Value)[]
        {
            ("OpenClaw", "openclaw"),
            ("Nanobot (not supported)", "nanobot"),
        };

        string? harness = null;

        while (harness == null)
        {
            if (isInitialSetup)
            {
                harness = await PromptSelectionHelper.PromptStringAsync(host,
                    "Choose harness:", harnessOptions, allowCancel: false, cancellationToken: ct);
            }
            else
            {
                var harnessResult = await PromptSelectionHelper.PromptStringWithBackAsync(host,
                    "Choose harness:", harnessOptions, cancellationToken: ct);
                if (harnessResult == null)
                {
                    result.IsChanged = false;
                    return result;
                }
                harness = harnessResult;
            }

            // For now only OpenClaw is supported; Nanobot is a placeholder
            if (harness == "nanobot")
            {
                host.AddMessage("[dim]Nanobot harness is not yet supported yet[/]");
                harness = null;
            }
        }

        ConfigSelectionHelper.PrintSubSection(host, harness, "harness setup");

        // ── Seed auth token from env var if not already set ──
        if (config.AuthToken == null)
            config.AuthToken = Environment.GetEnvironmentVariable("OPENCLAW_GATEWAY_TOKEN");

        // ── Loop over generic config items ──
        foreach (var item in _configItems)
        {
            if (await item.RunAsync(host, config, isInitialSetup, ct))
                changed = true;
        }

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
        result.Settings.Add(new ConfigSectionResult.SettingRecord("Gateway URL", config.GatewayUrl ?? "(not set)"));
        result.Settings.Add(new ConfigSectionResult.SettingRecord("Auth Token",
            config.AuthToken != null ? "••••••" : "(not set)"));

        result.IsChanged = changed;
        return result;
    }
}

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

    public async Task<bool> RunAsync(IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct)
    {
        bool changed = false;

        // ── Harness type ──
        var harnessOptions = new (string Name, string Value)[]
        {
            ("OpenClaw", "openclaw"),
            ("Nanobot (not supported)", "nanobot"),
        };

        string harness;
        if (isInitialSetup)
        {
            harness = await PromptSelectionHelper.PromptStringAsync(host,
                "Choose harness:", harnessOptions, "openclaw", allowCancel: false, ct);
        }
        else
        {
            var harnessResult = await PromptSelectionHelper.PromptStringWithBackAsync(host,
                "Choose harness:", harnessOptions, "openclaw", ct);
            if (harnessResult == null)
                return false;
            harness = harnessResult;
        }

        // For now only OpenClaw is supported; Nanobot is a placeholder
        if (harness == "nanobot")
        {
            host.AddMessage("[yellow]  Nanobot harness is not yet supported. Using OpenClaw.[/]");
            harness = "openclaw";
        }

        // ── Gateway URL ──
        var gatewayUrl = await PromptTextHelper.PromptAsync(host, "Gateway URL",
            config.GatewayUrl,
            v => Uri.TryCreate(v, UriKind.Absolute, out var uri) && (uri.Scheme == "ws" || uri.Scheme == "wss"),
            "Expected ws:// or wss:// URL",
            ct);
        if (gatewayUrl != null && gatewayUrl != config.GatewayUrl)
        {
            config.GatewayUrl = gatewayUrl;
            changed = true;
        }

        // ── Auth token ──
        var authToken = await PromptTextHelper.PromptAsync(host, "Auth token (OPENCLAW_GATEWAY_TOKEN env)",
            config.AuthToken ?? Environment.GetEnvironmentVariable("OPENCLAW_GATEWAY_TOKEN") ?? "",
            _ => true,
            null,
            ct, isSecret: true, allowEmpty: true);
        if (authToken != null)
        {
            var newValue = string.IsNullOrWhiteSpace(authToken) ? null : authToken;
            if (newValue != config.AuthToken)
            {
                config.AuthToken = newValue;
                changed = true;
            }
        }

        // ── TLS fingerprint (only for wss://) ──
        if (config.GatewayUrl.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            var tlsFingerprint = await PromptTextHelper.PromptAsync(host, "TLS cert fingerprint (optional, for wss:// pinning)",
                config.TlsFingerprint ?? "",
                _ => true,
                null,
                ct, allowEmpty: true);
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

        return changed;
    }
}

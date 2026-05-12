using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using StreamShell;

namespace OpenClawPTT.ConfigWizard;


public class ConfigSetupItem
{
    public string? Title;
    public string? FieldName;
    public Func<object, bool>? Validator;
    public string? ValidationHint;
    public bool IsSecrect = false;
    public bool IsEmptyToDefault = false;
    public bool IsClearAllowed = false;
}

/// <summary>Configures harness selection and gateway connection settings.</summary>
public sealed class HarnessConfigSection : IConfigSectionWizard
{
    public string Name => "Harness";
    public string Description => "Harness type and gateway connection";

    private readonly ConfigSetupItem[] _configItems;

    public HarnessConfigSection()
    {
        AppConfig appConfig;

        _configItems = new ConfigSetupItem[]
        {
            new ConfigSetupItem()
            {
                Title = "Gateway URL",
                FieldName = nameof(appConfig.GatewayUrl),
                Validator = v => Uri.TryCreate((string)v, UriKind.Absolute, out var uri) && (uri.Scheme == "ws" || uri.Scheme == "wss"),
                ValidationHint = "Expected ws:// or wss:// URL",
                DefaultValue = null,
            }
        };
    }

    public T GetConfigValue<T>(AppConfig appConfig, string fieldName)
    {
        //Reflection
    }


    public async Task<ConfigSectionResult> RunAsync(IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct)
    {
        var configSectionResult = new ConfigSectionResult();

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
                    $"Choose harness:", harnessOptions, allowCancel: false, cancellationToken: ct);
            }
            else
            {
                var harnessResult = await PromptSelectionHelper.PromptStringWithBackAsync(host,
                    "Choose harness:", harnessOptions, cancellationToken: ct);
                if (harnessResult == null)
                    return false;
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


        foreach (var configItem in _configItems)
        {
            var result = await PromptTextHelper.PromptAsync(host, configItem.Title ?? "",
                GetConfigValue<>(config, configItem.FieldName ?? ""),
                configItem.Validator ?? null,
                configItem.ValidationHint ?? "",
                ct,
                isSecret: configItem.IsSecrect,
                isEmptyToDefault: configItem.IsEmptyToDefault,
                allowClear: configItem.IsClearAllowed);
        }

        // ── Gateway URL ──

        // ── Auth token ──
        var authToken = await PromptTextHelper.PromptAsync(host, "Auth token (OPENCLAW_GATEWAY_TOKEN env)",
            config.AuthToken ?? Environment.GetEnvironmentVariable("OPENCLAW_GATEWAY_TOKEN") ?? "",
            value => !string.IsNullOrWhiteSpace(value),
            null,
            ct, isSecret: true, isEmptyToDefault: false);

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

        return changed;
    }
}

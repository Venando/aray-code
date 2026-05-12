using System;
using System.Collections.Generic;
using System.Text.Json;
using OpenClawPTT.ConfigWizard;
using Spectre.Console;
using System.Threading.Tasks;
using StreamShell;

namespace OpenClawPTT.Services;


public record Variant(string Name) : IVariant;

public class ConfigurationService : IConfigurationService
{
    private readonly IConfigStorage _storage;
    private readonly ModularConfigurationWizard _wizard;

    public ConfigurationService()
        : this(new FileConfigStorage())
    {
    }

    public ConfigurationService(IConfigStorage storage)
    {
        _storage = storage;
        _wizard = new ModularConfigurationWizard();
    }

    public async Task<AppConfig> LoadOrSetupAsync(IStreamShellHost shellHost, bool forceReconfigure = false, CancellationToken ct = default)
    {
        var cfg = _storage.Load();

        if (cfg is null)
        {
            shellHost.AddMessage("[dim]No configuration found — starting first-time setup.[/]");
            
            await shellHost.PromptSelection("Continue?", [new Variant("Yes") ]);

            cfg = await _wizard.RunInitialSetupAsync(shellHost, ct);
            _storage.Save(cfg);
            shellHost.AddMessage("[green]Configuration saved.[/]");
            return cfg;
        }

        var issues = Validate(cfg);
        bool needsSetup = issues.Count > 0 || forceReconfigure;

        if (issues.Count > 0)
        {
            shellHost.AddMessage("[cyan2]Configuration issues detected:[/]");
            foreach (var i in issues)
                shellHost.AddMessage($"  [grey]• {Markup.Escape(i)}[/]");
        }

        if (needsSetup)
        {
            if (forceReconfigure)
                shellHost.AddMessage("[cyan2]Starting setup wizard...[/]");
            else
                shellHost.AddMessage("[cyan2]Starting setup wizard to fix missing/invalid fields...[/]");

            cfg = await _wizard.RunInitialSetupAsync(shellHost, ct);
            _storage.Save(cfg);
            shellHost.AddMessage("[green]Configuration updated.[/]");
        }

        return cfg;
    }

    public async Task<AppConfig> ReconfigureAsync(IStreamShellHost shellHost, AppConfig existing, CancellationToken ct)
    {
        shellHost.AddMessage("[cyan2]Starting reconfiguration wizard...[/]");

        AppConfig newCfg;
        try
        {
            newCfg = await _wizard.RunReconfigureAsync(shellHost, existing, ct);
        }
        catch (OperationCanceledException)
        {
            return existing;
        }

        _storage.Save(newCfg);
        shellHost.AddMessage("[green]Configuration updated.[/]");
        return newCfg;
    }

    public AppConfig? Load() => _storage.Load();

    public void Save(AppConfig cfg) => _storage.Save(cfg);

    public List<string> Validate(AppConfig? cfg)
    {
        var issues = new List<string>();

        if (cfg == null)
        {
            issues.Add("Configuration is null.");
            return issues;
        }

        if (string.IsNullOrWhiteSpace(cfg.GatewayUrl))
            issues.Add("Gateway URL is required.");

        if (!Uri.TryCreate(cfg.GatewayUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "ws" && uri.Scheme != "wss"))
            issues.Add("Gateway URL must start with ws:// or wss://");

        if (string.IsNullOrWhiteSpace(cfg.AuthToken)
            && string.IsNullOrWhiteSpace(cfg.DeviceToken))
            issues.Add("Auth token or device token is required.");

        if (cfg.SampleRate is < 8000 or > 48000)
            issues.Add("Sample rate must be between 8000 and 48000.");

        if (cfg.ReconnectDelaySeconds <= 0)
            issues.Add("Reconnect delay must be positive.");

        if (!Enum.IsDefined(typeof(VisualMode), cfg.VisualMode))
            issues.Add($"VisualMode must be a defined value (current: {cfg.VisualMode}).");

        return issues;
    }
}

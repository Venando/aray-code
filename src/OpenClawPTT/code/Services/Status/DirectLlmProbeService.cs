using System;

namespace OpenClawPTT.Services;

/// <summary>
/// Owns the Direct LLM probing lifecycle: startup probe, config-change-triggered
/// re-probes, and last-called tracking. Extracted from AppRunner for SRP.
/// Disposable to unsubscribe from config change events.
/// </summary>
public sealed class DirectLlmProbeService : IDisposable
{
    private readonly IConfigurationService _configService;
    private readonly IStatusService _statusService;
    private readonly IColorConsole _console;
    private readonly IServiceFactory _factory;
    private string? _lastKnownLlmUrl;
    private string? _lastKnownLlmModel;
    private bool _disposed;

    public DirectLlmProbeService(
        IConfigurationService configService,
        IStatusService statusService,
        IColorConsole console,
        IServiceFactory factory)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

        _configService.ConfigSaved += OnConfigSaved;
    }

    /// <summary>
    /// Probes the Direct LLM endpoint and updates the status bar.
    /// Called on startup.
    /// </summary>
    public async Task ProbeOnStartupAsync(IDirectLlmService service, AppConfig config, CancellationToken ct = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DirectLlmProbeService));

        // Remember initial LLM config for change detection
        _lastKnownLlmUrl = config.DirectLlmUrl;
        _lastKnownLlmModel = config.DirectLlmModelName;

        await ProbeAndUpdateAsync(service, config, ct);
    }

    /// <summary>
    /// Probes the Direct LLM and updates the status bar.
    /// </summary>
    private async Task ProbeAndUpdateAsync(IDirectLlmService service, AppConfig config, CancellationToken ct)
    {
        if (!service.IsConfigured)
        {
            _statusService.SetDirectLlmStatus("\u2014", StatusColor.Yellow);
            return;
        }

        try
        {
            _statusService.SetDirectLlmStatus("Probing", StatusColor.Yellow);
            var ok = await service.ProbeAsync(ct);

            if (ok)
            {
                _statusService.SetDirectLlmStatus("OK", StatusColor.Green);
                _console.LogOk("llm", $"Direct LLM probed successfully ({config.DirectLlmModelName})");
            }
            else
            {
                _statusService.SetDirectLlmStatus("Failed", StatusColor.Red);
                _console.Log("llm", "Direct LLM probe failed — endpoint not reachable");
            }
        }
        catch (OperationCanceledException)
        {
            _statusService.SetDirectLlmStatus("\u2014", StatusColor.Yellow);
            throw;
        }
        catch (Exception ex)
        {
            _statusService.SetDirectLlmStatus("Error", StatusColor.Red);
            _console.LogError("llm", $"Direct LLM probe error: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-probes the Direct LLM whenever the app config is saved (e.g. via /appconfig).
    /// Filters to <c>DirectLlmUrl</c> / <c>DirectLlmModelName</c> changes only.
    /// </summary>
    private async void OnConfigSaved(AppConfig newCfg)
    {
        var llmUrl = newCfg.DirectLlmUrl;
        var llmModel = newCfg.DirectLlmModelName;

        bool urlChanged = !string.Equals(_lastKnownLlmUrl, llmUrl, StringComparison.OrdinalIgnoreCase);
        bool modelChanged = !string.Equals(_lastKnownLlmModel, llmModel, StringComparison.OrdinalIgnoreCase);

        if (!urlChanged && !modelChanged)
            return;

        _lastKnownLlmUrl = llmUrl;
        _lastKnownLlmModel = llmModel;

        try
        {
            using var freshService = _factory.CreateDirectLlmService(newCfg);
            await ProbeAndUpdateAsync(freshService, newCfg, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _console.LogError("llm", $"LLM re-probe failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _configService.ConfigSaved -= OnConfigSaved;
            _disposed = true;
        }
    }
}
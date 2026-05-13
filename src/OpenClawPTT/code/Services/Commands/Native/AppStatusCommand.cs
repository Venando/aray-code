using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.ConfigWizard;
using StreamShell;

namespace OpenClawPTT.Services.Commands;

/// <summary>
/// Native command: /appstatus — shows detailed status of gateway, TTS, STT, and Direct LLM services.
/// Uses StreamShell PromptSelection to present an expandable/collapsible status overview.
/// </summary>
public sealed class AppStatusCommand : ICommand
{
    private readonly IStreamShellHost _host;
    private readonly IStatusService _statusService;
    private readonly AppConfig _config;

    public string Name => "appstatus";
    public string Description => "Show detailed app status (GW/TTS/STT/LLM)";
    public CommandSource Source => CommandSource.Native;
    public ShellCommandType Type => ShellCommandType.Diagnostics;
    public string[]? Suggestions => null;

    public AppStatusCommand(
        IStreamShellHost host,
        IStatusService statusService,
        AppConfig config)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public Task ExecuteAsync(string[] args, Dictionary<string, string> namedArgs, CancellationToken ct = default)
    {
        // ── Gateway status ──────────────────────────────────────────────────
        var gwColor = _statusService.GetServiceStatus(ServiceKind.Gateway);
        var gwProvider = !string.IsNullOrWhiteSpace(_config.GatewayUrl)
            ? _config.GatewayUrl
            : "ws://localhost:8080/ws (default)";

        // ── TTS status ─────────────────────────────────────────────────────
        var ttsColor = _statusService.GetServiceStatus(ServiceKind.Tts);
        var ttsProvider = _config.TtsProvider.ToString();
        var ttsConfigured = _config.TtsOutputMode != "off";

        // ── STT status ─────────────────────────────────────────────────────
        var sttColor = _statusService.GetServiceStatus(ServiceKind.Stt);
        var sttProvider = _config.SttProvider ?? "built-in (gateway)";
        var sttModel = _config.FasterWhisperModel ?? _config.WhisperCppModel ?? "default";

        // ── Direct LLM status ──────────────────────────────────────────────
        var llmColor = _statusService.GetServiceStatus(ServiceKind.DirectLlm);
        var llmConfigured = !string.IsNullOrWhiteSpace(_config.DirectLlmUrl)
                          && !string.IsNullOrWhiteSpace(_config.DirectLlmModelName);
        var llmUrl = _config.DirectLlmUrl ?? "(not configured)";
        var llmModel = _config.DirectLlmModelName ?? "(not configured)";

        // ── Build PromptSelection variants ──────────────────────────────────
        var variants = new List<IVariant>(10)
        {
            MakeRow("Gateway", gwColor, gwProvider),
            MakeRow("TTS",    ttsColor, $"{ttsProvider} | Output: {_config.TtsOutputMode ?? "off"}"),
            MakeRow("STT",    sttColor, $"{sttProvider} | Model: {sttModel}"),
            MakeRow("DirectLLM", llmColor, llmConfigured
                ? $"{llmUrl} | Model: {llmModel}"
                : "(not configured)"),
        };

        // Spacer, then close option
        variants.Add(new ConfigVariant("", ""));
        variants.Add(new ConfigVariant("[bold]Close[/]", "__close__"));

        _ = _host.PromptSelection("App Status — select Close to dismiss", variants.ToArray());

        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds a single status row variant with a colored status dot and label.
    /// </summary>
    private static ConfigVariant MakeRow(string label, StatusColor? color, string detail)
    {
        var dotColor = color switch
        {
            StatusColor.Green => "green",
            StatusColor.Yellow => "yellow",
            StatusColor.Red => "red",
            _ => "grey",
        };

        var statusWord = color switch
        {
            StatusColor.Green => "OK",
            StatusColor.Yellow => "Pending",
            StatusColor.Red => "Error",
            _ => "Unknown",
        };

        return new ConfigVariant(
            $"[{dotColor}]\u25CF[/] [bold]{label}:[/] [{dotColor}]{statusWord}[/] [grey]\u2192 {detail}[/]",
            label); // value is unused — only the close option matters
    }
}

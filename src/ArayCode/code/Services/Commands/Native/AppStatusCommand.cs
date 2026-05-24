using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArayCode.Services.Themes;
using ArayCode.TTS;
using StreamShell;

namespace ArayCode.Services.Commands;

/// <summary>
/// Native command: /appstatus — shows detailed status of gateway, TTS, STT, and Direct LLM
/// in a bottom panel. Dismiss with Escape.
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

    public async Task ExecuteAsync(string[] args, Dictionary<string, string> namedArgs, CancellationToken ct = default)
    {
        StatusColor? Fetch(ServiceKind kind) => _statusService.GetServiceStatus(kind);

        using var panel = new AppStatusBottomPanel(_config, Fetch);
        _host.SetBottomPanel(panel);

        try
        {
            // Wait for Escape press or cancellation
            await Task.WhenAny(panel.WaitForDismissalAsync(), Task.Delay(Timeout.Infinite, ct));
        }
        catch (OperationCanceledException)
        {
            // Command cancelled — clean up below
        }
        finally
        {
            _host.ResetBottomPanel();
        }
    }
}

/// <summary>
/// Bottom panel that displays detailed status of gateway, TTS, STT, and Direct LLM.
/// Dismiss on Escape.
/// </summary>
public sealed class AppStatusBottomPanel : IBottomPanel
{
    private readonly AppConfig _config;
    private readonly Func<ServiceKind, StatusColor?> _getStatus;
    private readonly TaskCompletionSource _dismissedTcs = new();

    // Cached last-rendered status colors for dirty checking
    private StatusColor? _lastGw, _lastTts, _lastStt, _lastLlm;

    public AppStatusBottomPanel(AppConfig config, Func<ServiceKind, StatusColor?> getStatus)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _getStatus = getStatus ?? throw new ArgumentNullException(nameof(getStatus));
    }

    public int LineCount => 6;
    public bool IsDirty
    {
        get
        {
            var gwColor = _getStatus(ServiceKind.Gateway);
            var ttsColor = _getStatus(ServiceKind.Tts);
            var sttColor = _getStatus(ServiceKind.Stt);
            var llmColor = _getStatus(ServiceKind.DirectLlm);

            return gwColor != _lastGw
                || ttsColor != _lastTts
                || sttColor != _lastStt
                || llmColor != _lastLlm;
        }
    }

    public string? CurrentSuggestion => null;
    public bool ShowBottomSeparator => false;

    public IReadOnlyList<string> GetLines(string currentInput)
    {
        var gwColor = _getStatus(ServiceKind.Gateway);
        var ttsColor = _getStatus(ServiceKind.Tts);
        var sttColor = _getStatus(ServiceKind.Stt);
        var llmColor = _getStatus(ServiceKind.DirectLlm);

        return new[]
        {
            MakeLine("GW:",  gwColor, FormatGateway()),
            MakeLine("TTS:", ttsColor, FormatTts()),
            MakeLine("STT:", sttColor, FormatStt()),
            MakeLine("LLM:", llmColor, FormatLlm()),
            "",
            $"  [{ThemeProvider.Current.Tools.General.Muted}]Press Escape to dismiss[/]"
        };
    }

    public void ClearDirty()
    {
        // Sync cached colors so IsDirty stays false until the next actual change
        _lastGw = _getStatus(ServiceKind.Gateway);
        _lastTts = _getStatus(ServiceKind.Tts);
        _lastStt = _getStatus(ServiceKind.Stt);
        _lastLlm = _getStatus(ServiceKind.DirectLlm);
    }

    public bool TryHandleKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)
        {
            _dismissedTcs.TrySetResult();
            return true;
        }
        return false;
    }

    /// <summary>Returns a task that completes when the user presses Escape.</summary>
    public Task WaitForDismissalAsync() => _dismissedTcs.Task;

    public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public void Dispose() { }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string MakeLine(string label, StatusColor? color, string detail)
    {
        var dotColor = color switch
        {
            StatusColor.Green  => ThemeProvider.Current.Tools.Messages.Success,
            StatusColor.Yellow => ThemeProvider.Current.Tools.Messages.Warning,
            StatusColor.Red    => ThemeProvider.Current.Tools.Messages.Error,
            _ => ThemeProvider.Current.Tools.General.Muted,
        };
        var statusWord = color switch
        {
            StatusColor.Green  => "OK",
            StatusColor.Yellow => "Pending",
            StatusColor.Red    => "Error",
            _ => "Unknown",
        };
        return $"  [{dotColor}]\u25CF[/] [{ThemeProvider.Current.Tools.Messages.Emphasis}]{label}[/] [{dotColor}]{statusWord}[/] [{ThemeProvider.Current.Tools.General.Muted}]\u2192 {detail}[/]";
    }

    private string FormatGateway()
    {
        return !string.IsNullOrWhiteSpace(_config.GatewayUrl)
            ? _config.GatewayUrl
            : "ws://localhost:8080/ws (default)";
    }

    private string FormatTts()
    {
        if (_config.TtsProvider == TTS.TtsProviderType.Disabled)
            return "(disabled)";
        var provider = _config.TtsProvider.ToString();
        var mode = _config.TtsOutputMode ?? "off";
        return $"{provider} | Output: {mode}";
    }

    private string FormatStt()
    {
        var provider = _config.SttProvider ?? "built-in (gateway)";
        if (provider == "built-in (gateway)")
            return "(disabled)";
        var model = _config.FasterWhisperModel ?? _config.WhisperCppModel ?? "default";
        return $"{provider} | Model: {model}";
    }

    private string FormatLlm()
    {
        var configured = !string.IsNullOrWhiteSpace(_config.DirectLlmUrl)
                      && !string.IsNullOrWhiteSpace(_config.DirectLlmModelName);
        if (!configured)
            return "(disabled)";
        return $"{_config.DirectLlmUrl} | Model: {_config.DirectLlmModelName}";
    }
}

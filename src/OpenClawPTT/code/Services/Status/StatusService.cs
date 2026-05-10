using System;

namespace OpenClawPTT.Services;

/// <summary>
/// Tracks gateway and TTS status and renders a compact status line
/// on the right side of the StreamShell top separator.
///
/// Format: "  GW:[color]● label[/]  TTS:[color]● label[/]"
///
/// Thread-safe: all public methods synchronize on a lock before
/// mutating state and re-rendering.
/// </summary>
public sealed class StatusService : IStatusService
{
    private readonly IStreamShellHost _shellHost;
    private readonly object _lock = new();

    private string _gatewayLabel = "Starting";
    private StatusColor _gatewayColor = StatusColor.Yellow;
    private string _ttsLabel = "Starting";
    private StatusColor _ttsColor = StatusColor.Yellow;

    public StatusService(IStreamShellHost shellHost)
    {
        _shellHost = shellHost ?? throw new ArgumentNullException(nameof(shellHost));
        // Don't render in constructor — first caller will set real values immediately
    }

    public void SetGatewayStatus(string label, StatusColor color)
    {
        lock (_lock)
        {
            _gatewayLabel = label;
            _gatewayColor = color;
            Render();
        }
    }

    public void SetTtsStatus(string label, StatusColor color)
    {
        lock (_lock)
        {
            _ttsLabel = label;
            _ttsColor = color;
            Render();
        }
    }

    private void Render()
    {
        try
        {
            string rightText = $"  GW:[{ToMarkupColor(_gatewayColor)}]● {_gatewayLabel}[/]" +
                               $"  TTS:[{ToMarkupColor(_ttsColor)}]● {_ttsLabel}[/]";
            _shellHost.SetTopSeparator(rightText: rightText, repeatedCharacter: '─');
        }
        catch (Exception ex)
        {
            // Rendering is best-effort — never crash the caller if shell is disposed
            System.Diagnostics.Debug.WriteLine($"StatusService.Render failed: {ex.Message}");
        }
    }

    /// <summary>Maps <see cref="StatusColor"/> to Spectre.Console markup color names.</summary>
    private static string ToMarkupColor(StatusColor color) => color switch
    {
        StatusColor.Green => "green",
        StatusColor.Yellow => "yellow",
        StatusColor.Red => "red",
        _ => "yellow",
    };
}

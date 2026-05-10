namespace OpenClawPTT.Services;

/// <summary>
/// Tracks gateway and TTS status and renders a compact status line
/// on the right side of the StreamShell top separator.
///
/// Format: "  GW:[color]● label[/]  TTS:[color]● label[/]"
/// </summary>
public sealed class StatusService : IStatusService
{
    private readonly IStreamShellHost _shellHost;
    private string _gatewayLabel = "Starting";
    private string _gatewayColor = "yellow";
    private string _ttsLabel = "Starting";
    private string _ttsColor = "yellow";

    public StatusService(IStreamShellHost shellHost)
    {
        _shellHost = shellHost ?? throw new ArgumentNullException(nameof(shellHost));
        // Set initial separator on construction
        Render();
    }

    public void SetGatewayStatus(string label, string color)
    {
        _gatewayLabel = label;
        _gatewayColor = color;
        Render();
    }

    public void SetTtsStatus(string label, string color)
    {
        _ttsLabel = label;
        _ttsColor = color;
        Render();
    }

    private void Render()
    {
        string rightText = $"  GW:[{_gatewayColor}]● {_gatewayLabel}[/]  TTS:[{_ttsColor}]● {_ttsLabel}[/]";
        _shellHost.SetTopSeparator(rightText: rightText, repeatedCharacter: '─');
    }
}

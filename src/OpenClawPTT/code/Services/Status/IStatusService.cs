namespace OpenClawPTT.Services;

/// <summary>
/// Tracks application component status (gateway, TTS) and reflects it
/// in the StreamShell top separator.
/// </summary>
public interface IStatusService
{
    /// <summary>Update the gateway connection status displayed on the separator bar.</summary>
    void SetGatewayStatus(string label, string color);

    /// <summary>Update the TTS service status displayed on the separator bar.</summary>
    void SetTtsStatus(string label, string color);
}

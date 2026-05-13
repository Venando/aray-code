namespace OpenClawPTT;

using OpenClawPTT.Services;
using OpenClawPTT.Services.Diagnostics;
using OpenClawPTT.TTS;

/// <summary>
/// Partial class for TTS provider initialization and pipeline logic.
/// </summary>
public partial class AppRunner
{
    /// <summary>
    /// Initializes the TTS provider on a background thread.
    /// Updates status via <see cref="_statusService"/> as init progresses.
    /// </summary>
    private async Task<ITextToSpeech?> InitializeTtsProviderAsync(AppConfig cfg, CancellationToken ct)
    {
        try
        {
            _statusService.SetServiceStatus(ServiceKind.Tts, StatusColor.Yellow);
            _console.Log("tts", "Initializing TTS...");
            using var ttsService = _factory.CreateTtsService(cfg, _console);
            ct.ThrowIfCancellationRequested();

            if (ttsService.Provider != null)
            {
                _statusService.SetServiceStatus(ServiceKind.Tts, StatusColor.Green);
                _console.LogOk("tts", $"TTS connected ({ttsService.ProviderType})");
                return ttsService.ReleaseProvider();
            }

            // Provider is null (Edge with no key, etc.) — warn but don't error
            _statusService.SetServiceStatus(ServiceKind.Tts, StatusColor.Red);
            _console.Log("tts", $"TTS provider '{ttsService.ProviderType}' not configured — TTS disabled.");
            return null;
        }
        catch (OperationCanceledException)
        {
            _statusService.SetServiceStatus(ServiceKind.Tts, StatusColor.Red);
            throw;
        }
        catch (Exception ex)
        {
            _statusService.SetServiceStatus(ServiceKind.Tts, StatusColor.Red);
            _console.LogError("tts", $"TTS initialization failed: {ex.Message}");
            return null;
        }
    }
}

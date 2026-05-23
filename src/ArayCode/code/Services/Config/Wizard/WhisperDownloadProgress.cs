using System;
using System.Threading;
using System.Threading.Tasks;
using ArayCode.Services;
using ArayCode.Services.Themes;
using ArayCode.Transcriber;
using StreamShell;

namespace ArayCode.ConfigWizard;

/// <summary>
/// Handles whisper model download with bottom-panel progress.
/// Separate methods for Python openai-whisper (runs binary to trigger auto-download)
/// and C++ whisper.cpp (downloads .bin from HuggingFace with byte-level tracking).
/// </summary>
internal static class WhisperDownloadProgress
{
    // ── Python model download ────────────────────────────────────────

    /// <summary>
    /// Downloads a Python openai-whisper model by running the whisper binary
    /// with a tiny silent WAV to trigger auto-download. Shows progress in bottom panel.
    /// </summary>
    [Obsolete("Python openai-whisper is deprecated. Use DownloadFasterWhisperAsync() instead.")]
    public static async Task DownloadPythonAsync(
        IStreamShellHost host, string binaryPath,
        string modelName, CancellationToken ct)
    {
        var progressPanel = new DownloadProgressBottomPanel();
        host.SetBottomPanel(progressPanel);

        try
        {
            await WhisperCppModelManager.DownloadPythonModelAsync(
                binaryPath,
                modelName,
                progressCallback: (fileName, status, downloaded, total, complete) =>
                {
                    progressPanel.SetProgress(fileName, status, downloaded, total, complete);
                },
                ct: ct);

            await Task.Delay(500, ct);
        }
        catch (OperationCanceledException)
        {
            host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  Download cancelled.[/]");
        }
        catch (Exception ex)
        {
            host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Error}]  Download failed: {ex.Message}[/]");
        }
        finally
        {
            host.ResetBottomPanel();
        }
    }

    // ── faster-whisper model download (uv) ──────────────────────────

    /// <summary>
    /// Pre-downloads a faster-whisper model via <c>uv run</c>.
    /// Shows progress in the bottom panel.
    /// </summary>
    public static async Task DownloadFasterWhisperAsync(
        IStreamShellHost host, FasterWhisperModelManager modelManager,
        string modelName, CancellationToken ct)
    {
        var progressPanel = new DownloadProgressBottomPanel();
        host.SetBottomPanel(progressPanel);

        try
        {
            await modelManager.DownloadModelAsync(
                modelName,
                progressCallback: (fileName, status, downloaded, total, complete) =>
                {
                    progressPanel.SetProgress(fileName, status, downloaded, total, complete);
                },
                ct: ct);

            await Task.Delay(500, ct);
        }
        catch (OperationCanceledException)
        {
            host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  Download cancelled.[/]");
        }
        catch (Exception ex)
        {
            host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Error}]  Download failed: {ex.Message}[/]");
        }
        finally
        {
            host.ResetBottomPanel();
        }
    }

    // ── C++ model download ───────────────────────────────────────────

    /// <summary>
    /// Downloads a C++ whisper.cpp model from HuggingFace with byte-level
    /// progress displayed in the bottom panel.
    /// </summary>
    public static async Task DownloadCppAsync(
        IStreamShellHost host, WhisperCppModelManager modelManager,
        string modelName, CancellationToken ct)
    {
        var progressPanel = new DownloadProgressBottomPanel();
        host.SetBottomPanel(progressPanel);

        try
        {
            await modelManager.DownloadModelAsync(
                modelName,
                progressCallback: (fileName, status, downloaded, total, complete) =>
                {
                    progressPanel.SetProgress(fileName, status, downloaded, total, complete);
                },
                ct: ct);

            await Task.Delay(500, ct);
        }
        catch (OperationCanceledException)
        {
            host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  Download cancelled.[/]");
        }
        catch (Exception ex)
        {
            host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Error}]  Download failed: {ex.Message}[/]");
        }
        finally
        {
            host.ResetBottomPanel();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using OpenClawPTT.Transcriber;
using OpenClawPTT.Services.Themes;
using StreamShell;

namespace OpenClawPTT.ConfigWizard;

/// <summary>
/// Orchestrates the whisper configuration flow: binary detection,
/// model selection (delegated to <see cref="WhisperModelSelector"/>), and optional
/// model download (delegated to <see cref="WhisperDownloadProgress"/>).
/// Uses whisper.cpp (C++) binary only.
/// </summary>
public sealed class WhisperConfigFlow
{
    private const string CancelSentinel = "__cancel__";

    // ── Public entry point ───────────────────────────────────────────

    /// <summary>
    /// Runs the full whisper configuration flow.
    /// Returns true if any config value was changed.
    /// </summary>
    public async Task<bool> RunAsync(
        IStreamShellHost host, AppConfig config, CancellationToken ct)
    {
        var allBinaries = WhisperCppModelManager.FindAllWhisperBinaries();
        var modelManager = new WhisperCppModelManager(host, config.CustomDataDir ?? config.DataDir);

        // ── Step 1: Binary selection (loop until available or cancelled) ──
        string? resolvedBinaryPath;

        while (true)
        {
            var selected = await SelectBinaryAsync(host, allBinaries, ct);
            if (selected == null)
                return false;

            if (selected != null)
            {
                resolvedBinaryPath = selected;
                break;
            }

            host.AddMessage("");
            host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  ⚠ No whisper.cpp binary detected on your system.[/]");
            host.AddMessage("");
            host.AddMessage($"[{ThemeProvider.Current.Tools.General.Muted}]  Install whisper.cpp:[/]");
            host.AddMessage($"[{ThemeProvider.Current.Tools.General.Muted}]    Build from: https://github.com/ggerganov/whisper.cpp[/]");
            host.AddMessage($"[{ThemeProvider.Current.Tools.General.Muted}]    Add it to PATH, then re-run this configuration.[/]");
            host.AddMessage("");

            await Task.CompletedTask;
            // Loop back — user can try again
        }

        bool changed = false;
        if (resolvedBinaryPath != config.WhisperCppBinaryPath)
        {
            config.WhisperCppBinaryPath = resolvedBinaryPath;
            changed = true;
        }

        // ── Step 2: Model selection ──
        var modelResult = await WhisperModelSelector.SelectModelAsync(
            host, modelManager, config.WhisperCppModel, ct);
        if (modelResult == null)
        {
            if (changed)
                host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Success}]  Binary: whisper.cpp[/]");
            return changed;
        }

        if (modelResult != config.WhisperCppModel)
        {
            config.WhisperCppModel = modelResult;
            changed = true;
        }

        // ── Step 3: Download model if needed ──
        if (!modelManager.IsDownloaded(modelResult))
        {
            await WhisperDownloadProgress.DownloadCppAsync(
                host, modelManager, modelResult, ct);
        }

        // ── Log final config ──
        if (changed)
        {
            host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Success}]  Binary: whisper.cpp[/]");
            host.AddMessage($"[{ThemeProvider.Current.Tools.General.Muted}]  Path: {resolvedBinaryPath}[/]");
            host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Success}]  Model: {modelResult}[/]");
        }

        return changed;
    }

    /// <summary>
    /// Presents detected whisper.cpp binaries as a PromptSelection.
    /// </summary>
    private static async Task<string?> SelectBinaryAsync(
        IStreamShellHost host, IReadOnlyList<WhisperBinaryInfo> allBinaries, CancellationToken ct)
    {
        var cppBinaries = allBinaries
            .Where(b => b.Type == WhisperType.Cpp)
            .ToList();

        if (cppBinaries.Count == 0)
        {
            // No binary found — let caller show install instructions
            return "";
        }

        if (cppBinaries.Count == 1)
        {
            // Only one option — use it directly
            return cppBinaries[0].Path;
        }

        // Multiple binaries — let user pick
        var variants = new List<IVariant>();
        foreach (var binary in cppBinaries)
        {
            variants.Add(new ConfigVariant(
                $"whisper.cpp at [green]{binary.Path}[/]", binary.Path));
        }
        variants.Add(new ConfigVariant("", ""));
        variants.Add(new ConfigVariant($"[{ThemeProvider.Current.Tools.General.Muted}]Cancel[/]", CancelSentinel));

        var result = await host.PromptSelection("Select whisper.cpp binary:", variants.ToArray());
        if (result is not { Length: > 0 } || result[0] is not ConfigVariant cv)
            return null;

        return cv.Value == CancelSentinel ? null : cv.Value;
    }
}

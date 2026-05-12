using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using OpenClawPTT.Transcriber;
using StreamShell;

namespace OpenClawPTT.ConfigWizard;

/// <summary>
/// Handles whisper model selection via PromptSelection.
/// Unified flow for both Python openai-whisper (all models selectable, auto-download)
/// and C++ whisper.cpp (downloaded models selectable, non-downloaded downloadable).
/// </summary>
internal static class WhisperModelSelector
{
    private const string CancelSentinel = "__cancel__";

    /// <summary>
    /// Presents available model options in PromptSelection.
    /// Returns the selected model name, or null if cancelled.
    /// </summary>
    public static async Task<string?> SelectModelAsync(
        IStreamShellHost host, WhisperCppModelManager modelManager,
        bool isPython, string? currentModel, CancellationToken ct)
    {
        string? result = null;

        while (result == null)
        {
            ct.ThrowIfCancellationRequested();

            var variants = new List<IVariant>();

            if (isPython)
            {
                BuildPythonVariants(variants, currentModel);
            }
            else
            {
                BuildCppVariants(variants, modelManager, currentModel);
            }

            variants.Add(new ConfigVariant("", ""));
            variants.Add(new ConfigVariant("[grey]Cancel[/]", CancelSentinel));

            var promptText = isPython
                ? "Select model (auto-downloaded on first use):"
                : "Select model, download, or cancel:";

            var selection = await host.PromptSelection(promptText, variants.ToArray());
            if (selection is not { Length: > 0 } || selection[0] is not ConfigVariant cv)
                return result;

            var choice = cv.Value;

            if (choice == CancelSentinel)
                return result;

            if (choice.StartsWith("use:"))
            {
                result = choice["use:".Length..];
            }
            else if (choice.StartsWith("download:"))
            {
                var modelName = choice["download:".Length..];
                await WhisperDownloadProgress.DownloadCppAsync(
                    host, modelManager, modelName, ct);
                result = modelName;
            }
        }

        return result;
    }

    // ── Python model list ────────────────────────────────────────────

    private static void BuildPythonVariants(List<IVariant> variants, string? currentModel)
    {
        foreach (var info in WhisperCppModelManager.AvailableModels)
        {
            var isActive = info.Name == currentModel;
            var name = isActive
                ? $"[green]● {info.Name}[/] [cyan][[active]][/] [grey]({info.Description})[/]"
                : $"[green]● {info.Name}[/] [grey]({info.Description})[/]";
            variants.Add(new ConfigVariant(name, $"use:{info.Name}"));
        }
    }

    // ── C++ model list (downloaded + downloadable) ───────────────────

    private static void BuildCppVariants(List<IVariant> variants, WhisperCppModelManager modelManager, string? currentModel)
    {
        var downloadedModels = modelManager.GetDownloadedModels();

        // Downloaded models
        foreach (var model in downloadedModels)
        {
            var info = WhisperCppModelManager.AvailableModels
                .FirstOrDefault(m => m.Name == model);
            var desc = info != null ? $" [grey]({info.Description})[/]" : "";
            var isActive = model == currentModel;
            var activeMarker = isActive ? " [cyan][[active]][/]" : "";
            variants.Add(new ConfigVariant(
                $"[green]✓ {model}[/]{desc}{activeMarker}",
                $"use:{model}"));
        }

        // Non-downloaded models — download option
        var notDownloaded = WhisperCppModelManager.AvailableModels
            .Where(m => !downloadedModels.Contains(m.Name))
            .ToList();

        if (notDownloaded.Count == 0)
            return;

        if (variants.Count > 0)
        {
            variants.Add(new ConfigVariant("", ""));
            variants.Add(new ConfigVariant("[bold cyan]── Available for download ──[/]", "__header__"));
        }

        foreach (var model in notDownloaded)
        {
            variants.Add(new ConfigVariant(
                $"[grey]⬇ {model.Name} ({model.Description})[/]",
                $"download:{model.Name}"));
        }
    }
}

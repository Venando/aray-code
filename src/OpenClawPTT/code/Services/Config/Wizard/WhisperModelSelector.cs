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
/// Unified flow for both Python openai-whisper (models cached in ~/.cache/whisper/)
/// and C++ whisper.cpp (models stored as .bin files).
/// Showes cached models as selectable, non-cached as downloadable, and allows removal.
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
                BuildPythonVariants(variants, currentModel);
            else
                BuildCppVariants(variants, modelManager, currentModel);

            variants.Add(new ConfigVariant("", ""));
            variants.Add(new ConfigVariant("[grey]Cancel[/]", CancelSentinel));

            var selection = await host.PromptSelection(
                "Select model, download, remove, or cancel:",
                variants.ToArray());

            if (selection is not { Length: > 0 } || selection[0] is not ConfigVariant cv)
                return result;

            var choice = cv.Value;

            if (choice == CancelSentinel)
                return result;

            if (choice.StartsWith("use:") || choice.StartsWith("download:"))
            {
                result = choice[(choice.StartsWith("use:") ? "use:" : "download:").Length..];
            }
            else if (choice.StartsWith("remove:"))
            {
                var modelName = choice["remove:".Length..];
                var confirm = await host.PromptSelection(
                    $"Remove model '{modelName}'?",
                    [new ConfigVariant("[red]Yes, remove[/]", "yes"),
                     new ConfigVariant("Cancel", "no")]);

                if (confirm is { Length: > 0 } && confirm[0] is ConfigVariant cv2 && cv2.Value == "yes")
                {
                    bool removed = isPython
                        ? WhisperCppModelManager.DeletePythonModel(modelName)
                        : modelManager.DeleteModel(modelName);

                    if (removed)
                        host.AddMessage($"[green]  ✓ Removed {modelName}[/]");
                }
            }
        }

        return result;
    }

    // ── Python model list ────────────────────────────────────────────

    private static void BuildPythonVariants(List<IVariant> variants, string? currentModel)
    {
        var allModels = WhisperCppModelManager.AvailableModels;

        // Cached (downloaded) models — selectable
        var cached = allModels.Where(m => WhisperCppModelManager.IsPythonModelCached(m.Name)).ToList();
        foreach (var info in cached)
        {
            var isActive = info.Name == currentModel;
            var activeMarker = isActive ? " [cyan][[active]][/]" : "";
            variants.Add(new ConfigVariant(
                $"[green]✓ {info.Name}[/] [grey]({info.Description})[/]{activeMarker}",
                $"use:{info.Name}"));
        }

        // Non-cached models — download option
        var notCached = allModels.Where(m => !WhisperCppModelManager.IsPythonModelCached(m.Name)).ToList();
        if (notCached.Count > 0)
        {
            if (variants.Count > 0)
            {
                variants.Add(new ConfigVariant("", ""));
                variants.Add(new ConfigVariant("[bold cyan]── Available for download ──[/]", "__header__"));
            }

            foreach (var info in notCached)
            {
                variants.Add(new ConfigVariant(
                    $"[grey]⬇ {info.Name} ({info.Description})[/]",
                    $"download:{info.Name}"));
            }
        }

        // Remove cached models
        if (cached.Count > 0)
        {
            variants.Add(new ConfigVariant("", ""));
            variants.Add(new ConfigVariant("[bold red]── Remove ──[/]", "__remove_header__"));
            foreach (var info in cached)
            {
                variants.Add(new ConfigVariant(
                    $"[red]Remove: {info.Name}[/]",
                    $"remove:{info.Name}"));
            }
        }
    }

    // ── C++ model list (downloaded + downloadable) ───────────────────

    private static void BuildCppVariants(List<IVariant> variants, WhisperCppModelManager modelManager, string? currentModel)
    {
        var downloadedModels = modelManager.GetDownloadedModels();

        // Downloaded models — selectable
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

        if (notDownloaded.Count > 0)
        {
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

        // Remove downloaded models
        if (downloadedModels.Count > 0)
        {
            variants.Add(new ConfigVariant("", ""));
            variants.Add(new ConfigVariant("[bold red]── Remove ──[/]", "__remove_header__"));
            foreach (var model in downloadedModels)
            {
                variants.Add(new ConfigVariant(
                    $"[red]Remove: {model}[/]",
                    $"remove:{model}"));
            }
        }
    }
}

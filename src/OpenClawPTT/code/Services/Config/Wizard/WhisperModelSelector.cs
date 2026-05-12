using System;
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
/// Shows available models as selectable, non-cached as downloadable, and allows removal.
/// </summary>
internal static class WhisperModelSelector
{
    private const string CancelSentinel = "__cancel__";

    /// <summary>
    /// Presents available model options in PromptSelection.
    /// </summary>
    public static async Task<string?> SelectModelAsync(
        IStreamShellHost host, WhisperCppModelManager modelManager,
        bool isPython, string? currentModel, CancellationToken ct)
    {
        // Snapshot which models are already downloaded/cached once
        // to avoid redundant filesystem calls on every loop iteration.
        var availableModels = isPython
            ? GetPythonCachedModels()
            : new HashSet<string>(modelManager.GetDownloadedModels(), StringComparer.Ordinal);

        string? result = null;

        while (result == null)
        {
            ct.ThrowIfCancellationRequested();

            var variants = BuildVariants(availableModels, isPython, currentModel);

            variants.Add(new ConfigVariant("", ""));
            variants.Add(new ConfigVariant("[grey]Cancel[/]", CancelSentinel));

            // Pre-size then build — avoid re-allocation in ToArray
            var variantsArray = variants.ToArray();

            var selection = await host.PromptSelection(
                "Select model, download, remove, or cancel:",
                variantsArray);

            if (selection is not { Length: > 0 } || selection[0] is not ConfigVariant cv)
                return result;

            var choice = cv.Value;

            if (choice == CancelSentinel)
                return result;

            if (choice.StartsWith("use:") || choice.StartsWith("download:"))
            {
                result = choice[(choice.StartsWith("use:") ? "use:" : "download:").Length..];
                // Snapshot the available set again after download/selection
                // since the available set may have changed
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
                    {
                        host.AddMessage($"[green]  ✓ Removed {modelName}[/]");
                        // Re-snapshot since the set changed
                        availableModels = isPython
                            ? GetPythonCachedModels()
                            : new HashSet<string>(modelManager.GetDownloadedModels(), StringComparer.Ordinal);
                    }
                }
            }
            // Header/separator items fall through — loop re-runs with same snapshot
        }

        return result;
    }

    // ── Shared variant builder (DRY) ─────────────────────────────────

    /// <summary>
    /// Builds the variant list from the available models snapshot.
    /// Three sections: downloaded (use), downloadable (download), remove.
    /// Same structure for both Python and C++.
    /// </summary>
    private static List<IVariant> BuildVariants(
        HashSet<string> availableModels, bool isPython, string? currentModel)
    {
        var allModels = WhisperCppModelManager.AvailableModels;
        // Pre-allocate capacity: 12 models × up to 3 sections + separators + cancel
        var variants = new List<IVariant>(40);

        // ── Downloaded/cached models ──
        foreach (var info in allModels)
        {
            if (!availableModels.Contains(info.Name))
                continue;

            var isActive = info.Name == currentModel;
            var activeMarker = isActive ? " [cyan][[active]][/]" : "";
            variants.Add(new ConfigVariant(
                $"[green]✓ {info.Name}[/] [grey]({info.Description})[/]{activeMarker}",
                $"use:{info.Name}"));
        }

        // ── Non-downloaded models (download option) ──
        var notAvailable = allModels
            .Where(m => !availableModels.Contains(m.Name))
            .ToList();

        if (notAvailable.Count > 0)
        {
            if (variants.Count > 0)
            {
                variants.Add(new ConfigVariant("", ""));
                variants.Add(new ConfigVariant("[bold cyan]── Available for download ──[/]", "__header__"));
            }

            foreach (var info in notAvailable)
            {
                variants.Add(new ConfigVariant(
                    $"[grey]⬇ {info.Name} ({info.Description})[/]",
                    $"download:{info.Name}"));
            }
        }

        // ── Remove downloaded models ──
        if (availableModels.Count > 0)
        {
            variants.Add(new ConfigVariant("", ""));
            variants.Add(new ConfigVariant("[bold red]── Remove ──[/]", "__remove_header__"));
            foreach (var info in allModels)
            {
                if (!availableModels.Contains(info.Name))
                    continue;
                variants.Add(new ConfigVariant(
                    $"[red]Remove: {info.Name}[/]",
                    $"remove:{info.Name}"));
            }
        }

        return variants;
    }

    // ── Python cache snapshot ────────────────────────────────────────

    /// <summary>
    /// Reads the Python whisper cache directory once and returns all cached model names.
    /// Replaces repeated IsPythonModelCached calls (each = File.Exists) with a single
    /// directory scan.
    /// </summary>
    private static HashSet<string> GetPythonCachedModels()
    {
        var cacheDir = WhisperCppModelManager.GetPythonCacheDir();
        if (!System.IO.Directory.Exists(cacheDir))
            return new HashSet<string>(StringComparer.Ordinal);

        var cached = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in System.IO.Directory.EnumerateFiles(cacheDir, "*.pt"))
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(file);
            if (name != null)
                cached.Add(name);
        }
        return cached;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using OpenClawPTT.Transcriber;
using OpenClawPTT.TTS;
using OpenClawPTT.TTS.Providers;
using Spectre.Console;
using StreamShell;

namespace OpenClawPTT.ConfigWizard;

/// <summary>
/// Orchestrates the Coqui TTS (uv) configuration flow: model selection,
/// pre-download, and removal via <see cref="CoquiTtsModelManager"/>.
/// </summary>
public sealed class CoquiTtsConfigFlow
{
    // Action marker prefixes used in variant values (SRP: kept as constants for clarity)
    private const string ActionUse = "use:";
    private const string ActionDownload = "download:";
    private const string ActionRemove = "remove:";
    private const string CancelSentinel = "__cancel__";

    public async Task<bool> RunAsync(
        IStreamShellHost host, AppConfig config, CancellationToken ct)
    {
        var dataDir = config.CustomDataDir ?? config.DataDir;

        // Ensure the pyproject.toml exists — uv needs it for dependency resolution
        var env = new CoquiUvEnvironment(
            dataDir,
            config.CoquiModelName ?? "tts_models/multilingual/mxtts/vits",
            config.CoquiModelPath,
            config.CoquiConfigPath,
            config.EspeakNgPath);
        env.EnsureProjectFiles();

        // ── Phase 1: Gate — validate uv and Python before anything else ──
        await ValidateEnvironmentAsync(host, dataDir, ct);

        // ── Phase 2: Model selection ──
        var modelManager = new CoquiTtsModelManager(dataDir, host);
        var modelResult = await SelectModelAsync(
            host, modelManager, config, config.CoquiModelName, dataDir, ct);

        if (modelResult == null)
            return false;

        // Some models (e.g. jenny) are distributed as ZIP archives.
        // Extract them now so the TTS service can find the files.
        var extractedZips = CoquiTtsModelManager.ExtractModelZips(modelResult);
        if (extractedZips > 0)
            host.AddMessage($"[green]    ✓ Extracted {extractedZips} archive(s) for {modelResult}[/]");

        bool modelChanged = modelResult != config.CoquiModelName;

        // ── Phase 3: Download (prompt when not cached, regardless of model change) ──
        var downloadSucceeded = await HandleDownloadAsync(
            host, modelManager, modelResult, modelChanged, ct);

        // ── Phase 4: Apply config change AFTER download resolution ──
        if (modelChanged)
        {
            config.CoquiModelName = modelResult;
            config.SttProvider = null; // ensure TTS provider is CoquiUv
            host.AddMessage($"[green]  Model: {modelResult}[/]");

            if (!downloadSucceeded && !CoquiTtsModelManager.IsModelCached(modelResult))
            {
                host.AddMessage("[yellow]  ⚠ Model not cached yet — TTS won't work until downloaded.[/]");
            }
        }

        return modelChanged;
    }

    // ── Phase 1: Environment validation ─────────────────────────────

    /// <summary>
    /// Checks uv availability and Python version compatibility.
    /// Extracted from <see cref="RunAsync"/> to keep the main flow focused (SRP).
    /// </summary>
    private static async Task ValidateEnvironmentAsync(
        IStreamShellHost host, string? dataDir, CancellationToken ct)
    {
        var uvAvailable = CoquiUvEnvironment.IsUvAvailable();
        if (!uvAvailable)
        {
            host.AddMessage($"[yellow]  ⚠ uv (Python package manager) is not installed.[/]");
            host.AddMessage($"[grey]    Install: {CoquiUvEnvironment.GetInstallInstructions()}[/]");
            host.AddMessage("[grey]    You can still select a model from the built-in list below.[/]");
            host.AddMessage("[grey]    But you'll need uv to actually use Coqui TTS.[/]");
            host.AddMessage("");
            return;
        }

        // Quick Python version check — uses uv python list (fast, no downloads)
        host.AddMessage("[grey]    Checking Python environment...[/]");
        var versionResult = await CoquiUvEnvironment.ValidatePythonVersionAsync(
            dataDir,
            onProgress: msg => host.AddMessage($"[grey]      {msg}[/]"),
            ct);

        if (versionResult.Ok)
        {
            host.AddMessage($"[green]    ✓ Python {versionResult.PythonVersion} found at {versionResult.PythonPath}[/]");
        }
        else
        {
            host.AddMessage($"[red]    ✗ Python environment check failed: {versionResult.Error}[/]");
            host.AddMessage("[yellow]    Coqui TTS requires Python >=3.9 and <3.12.[/]");
            host.AddMessage("[grey]    uv can download the right Python automatically when running TTS.[/]");
            host.AddMessage("[grey]    You can still select a model from the built-in list below,[/]");
            host.AddMessage("[grey]    but TTS won't work until a compatible Python is available.[/]");
            host.AddMessage("");
        }
    }

    // ── Phase 3: Download handling ──────────────────────────────────

    /// <summary>
    /// Prompts the user to download the model if not cached, then
    /// executes the download. Returns true if the model is cached after
    /// this phase (either was already cached or was successfully downloaded).
    /// Extracted from <see cref="RunAsync"/> (SRP).
    /// </summary>
    private static async Task<bool> HandleDownloadAsync(
        IStreamShellHost host, CoquiTtsModelManager modelManager,
        string modelName, bool modelChanged, CancellationToken ct)
    {
        if (CoquiTtsModelManager.IsModelCached(modelName))
            return true;

        var promptText = modelChanged
            ? $"Model '{modelName}' is not cached. Download now?"
            : $"Model '{modelName}' (already configured) is not cached. Download now?";

        var shouldDownload = await host.PromptSelection(
            promptText,
            [new ConfigVariant("[green]Download now[/]", "download"),
             new ConfigVariant("[yellow]Select without downloading[/]", "select"),
             new ConfigVariant("[grey]Cancel[/]", "cancel")]);

        if (shouldDownload is not { Length: > 0 } || shouldDownload[0] is not ConfigVariant cv)
            return false;

        switch (cv.Value)
        {
            case "cancel":
                return false;
            case "download":
                return await ExecuteDownloadAsync(host, modelManager, modelName, ct);
            default: // "select" or unknown
                host.AddMessage("[grey]  Model selected without downloading. Use /reconfigure to download later.[/]");
                return false;
        }
    }

    /// <summary>
    /// Executes the model download with a bottom panel progress indicator.
    /// Returns true on success, false on cancellation or failure.
    /// </summary>
    private static async Task<bool> ExecuteDownloadAsync(
        IStreamShellHost host, CoquiTtsModelManager modelManager,
        string modelName, CancellationToken ct)
    {
        try
        {
            await DownloadModelAsync(host, modelManager, modelName, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            host.AddMessage("[yellow]  Download cancelled — model selected but not downloaded.[/]");
            return false;
        }
        catch (Exception ex)
        {
            host.AddMessage($"[red]  Download failed: {Markup.Escape(ex.Message)}[/]");
            host.AddMessage("[yellow]  Model selected but download failed. You can retry later.[/]");
            return false;
        }
    }

    // ── Model selection ─────────────────────────────────────────────

    /// <summary>
    /// Presents a live model list from Coqui TTS with cached/available/
    /// removable states. Returns the selected model name, or null if cancelled.
    /// </summary>
    internal static async Task<string?> SelectModelAsync(
        IStreamShellHost host, CoquiTtsModelManager modelManager,
        AppConfig config, string? currentModel, string? dataDir, CancellationToken ct)
    {
        var (allModels, cachedModels) = await FetchModelListsAsync(host, dataDir, ct);

        if (allModels.Count == 0)
        {
            return await HandleNoModelsAsync(host, currentModel);
        }

        string? result = null;

        while (result == null)
        {
            ct.ThrowIfCancellationRequested();

            var variants = BuildVariantList(allModels, cachedModels, currentModel);
            variants.Add(new ConfigDecoration(""));
            variants.Add(new ConfigVariant("[grey]Cancel[/]", CancelSentinel));

            var selection = await host.PromptSelection(
                "Select Coqui TTS model, download, remove, or cancel:",
                variants.ToArray(), new SelectionInfo { Rows = 16 });

            if (selection is not { Length: > 0 } || selection[0] is not ConfigVariant cv)
                return result;

            result = await HandleSelectionAsync(
                host, modelManager, cv, allModels, cachedModels,
                dataDir, currentModel, ct);

            // Only refresh model lists after a remove action
            if (result == null && cachedModels.Count > 0 && cv.Value.StartsWith(ActionRemove))
            {
                (allModels, cachedModels) = await FetchModelListsAsync(host, dataDir, ct);
            }
        }

        return result;
    }

    /// <summary>
    /// Handles the case where no models are available from the live fetch.
    /// If uv is broken but a model is already configured, keep it.
    /// </summary>
    private static async Task<string?> HandleNoModelsAsync(
        IStreamShellHost host, string? currentModel)
    {
        host.AddMessage("");
        host.AddMessage("[red]  ✗ No models available — live fetch from coqui/TTS failed.[/]");
        host.AddMessage("[grey]    Check the errors above and fix uv/Python issues.[/]");
        host.AddMessage("[grey]    Then re-run /reconfigure TTS to select a model.[/]");

        var errorDetail = CoquiTtsModelManager.LastFetchErrorDetail;
        if (IsUvBuildError(errorDetail))
        {
            if (!string.IsNullOrEmpty(currentModel))
                host.AddMessage($"[grey]    Current model: {currentModel}[/]");
            return currentModel;
        }

        return null;
    }

    private static bool IsUvBuildError(string? errorDetail) =>
        !string.IsNullOrEmpty(errorDetail) &&
        (errorDetail.Contains("Failed to build", StringComparison.Ordinal) ||
         errorDetail.Contains("build_wheel", StringComparison.Ordinal) ||
         errorDetail.Contains("build backend", StringComparison.Ordinal));

    /// <summary>
    /// Fetches both available and cached model lists in one call.
    /// Extracted to deduplicate the paired fetch pattern (DRY).
    /// </summary>
    private static async Task<(IReadOnlyList<CoquiTtsModelInfo> AllModels, HashSet<string> CachedModels)> FetchModelListsAsync(
        IStreamShellHost host, string? dataDir, CancellationToken ct)
    {
        var allModels = await CoquiTtsModelManager.GetAvailableModelsAsync(host, dataDir, ct);
        var cachedModels = new HashSet<string>(
            await CoquiTtsModelManager.GetCachedModelsAsync(host, dataDir, ct),
            StringComparer.Ordinal);
        return (allModels, cachedModels);
    }

    /// <summary>
    /// Routes a user selection to the correct action: use, download, remove, or none.
    /// Returns the selected model name or null to re-prompt.
    /// </summary>
    private static async Task<string?> HandleSelectionAsync(
        IStreamShellHost host, CoquiTtsModelManager modelManager,
        ConfigVariant cv, IReadOnlyList<CoquiTtsModelInfo> allModels,
        HashSet<string> cachedModels, string? dataDir,
        string? currentModel, CancellationToken ct)
    {
        var choice = cv.Value;

        if (choice == CancelSentinel)
            return null; // signals "finished, no selection"

        if (choice.StartsWith(ActionUse) || choice.StartsWith(ActionDownload))
            return choice[(choice.StartsWith(ActionUse) ? ActionUse : ActionDownload).Length..];

        if (choice.StartsWith(ActionRemove))
        {
            return await HandleRemoveAsync(host, modelManager, allModels,
                cachedModels, choice[ActionRemove.Length..], dataDir, ct);
        }

        return null; // unknown — re-prompt
    }

    /// <summary>
    /// Handles the remove-model flow: confirmation prompt, deletion, and cache refresh.
    /// </summary>
    private static async Task<string?> HandleRemoveAsync(
        IStreamShellHost host, CoquiTtsModelManager modelManager,
        IReadOnlyList<CoquiTtsModelInfo> allModels,
        HashSet<string> cachedModels, string modelName,
        string? dataDir, CancellationToken ct)
    {
        var confirm = await host.PromptSelection(
            $"Remove model '{modelName}'?",
            [new ConfigVariant("[red]Yes, remove[/]", "yes"),
             new ConfigVariant("Cancel", "no")]);

        if (confirm is not { Length: > 0 } || confirm[0] is not ConfigVariant cv || cv.Value != "yes")
            return null; // re-prompt

        bool removed = CoquiTtsModelManager.DeleteModel(modelName);
        if (removed)
        {
            host.AddMessage($"[green]  ✓ Removed {modelName}[/]");
        }
        else
        {
            host.AddMessage($"[grey]  Could not remove {modelName} (not found or partial cache).[/]");
        }

        return null; // re-prompt after removal
    }

    // ── Download execution ──────────────────────────────────────────

    private static async Task DownloadModelAsync(
        IStreamShellHost host, CoquiTtsModelManager modelManager,
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
        finally
        {
            host.ResetBottomPanel();
        }
    }

    // ── Variant builder ─────────────────────────────────────────────

    /// <summary>
    /// Builds the complete list of selection variants from available,
    /// cached, and current models. Extracted from <see cref="SelectModelAsync"/>
    /// to keep selection loop focused (SRP).
    /// </summary>
    private static List<IVariantEntry> BuildVariantList(
        IReadOnlyList<CoquiTtsModelInfo> allModels,
        HashSet<string> cachedModels, string? currentModel)
    {
        var variants = new List<IVariantEntry>(allModels.Count * 3 + 10);

        variants.Add(new ConfigDecoration(
            $"[bold green]── {allModels.Count} models from Coqui TTS ──[/]"));

        AddCachedModelVariants(variants, allModels, cachedModels, currentModel);
        AddDownloadableModelVariants(variants, allModels, cachedModels);
        AddRemoveVariants(variants, allModels, cachedModels);

        return variants;
    }

    private static void AddCachedModelVariants(
        List<IVariantEntry> variants,
        IReadOnlyList<CoquiTtsModelInfo> allModels,
        HashSet<string> cachedModels, string? currentModel)
    {
        foreach (var info in allModels)
        {
            if (!cachedModels.Contains(info.Name))
                continue;

            var isActive = info.Name == currentModel;
            var activeMarker = isActive ? " [cyan][[active]][/]" : "";
            variants.Add(new ConfigVariant(
                $"[green]✓ {info.Name}[/] [grey]({info.Description})[/]{activeMarker}",
                $"{ActionUse}{info.Name}"));
        }
    }

    private static void AddDownloadableModelVariants(
        List<IVariantEntry> variants,
        IReadOnlyList<CoquiTtsModelInfo> allModels,
        HashSet<string> cachedModels)
    {
        var notCached = allModels
            .Where(m => !cachedModels.Contains(m.Name))
            .ToList();

        if (notCached.Count == 0)
            return;

        if (variants.Count > 1) // >1 because of the header
        {
            variants.Add(new ConfigDecoration(""));
            variants.Add(new ConfigDecoration("[bold cyan]── Available for download ──[/]"));
        }

        foreach (var info in notCached)
        {
            variants.Add(new ConfigVariant(
                $"[grey]⬇ {info.Name} ({info.Description})[/]",
                $"{ActionDownload}{info.Name}"));
        }
    }

    private static void AddRemoveVariants(
        List<IVariantEntry> variants,
        IReadOnlyList<CoquiTtsModelInfo> allModels,
        HashSet<string> cachedModels)
    {
        if (cachedModels.Count == 0)
            return;

        variants.Add(new ConfigDecoration(""));
        variants.Add(new ConfigDecoration("[bold red]── Remove ──[/]"));
        foreach (var info in allModels)
        {
            if (!cachedModels.Contains(info.Name))
                continue;
            variants.Add(new ConfigVariant(
                $"[red]Remove: {info.Name}[/]",
                $"{ActionRemove}{info.Name}"));
        }
    }
}

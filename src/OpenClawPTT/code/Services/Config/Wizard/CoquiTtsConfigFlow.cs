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
        var uvAvailable = CoquiUvEnvironment.IsUvAvailable();
        if (!uvAvailable)
        {
            host.AddMessage($"[yellow]  ⚠ uv (Python package manager) is not installed.[/]");
            host.AddMessage($"[grey]    Install: {CoquiUvEnvironment.GetInstallInstructions()}[/]");
            host.AddMessage("[grey]    You can still select a model from the built-in list below.[/]");
            host.AddMessage("[grey]    But you'll need uv to actually use Coqui TTS.[/]");
            host.AddMessage("");
        }
        else
        {
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

        // ── Phase 2: Model selection ──
        var modelManager = new CoquiTtsModelManager(dataDir, host);
        var modelResult = await SelectModelAsync(
            host, modelManager, config, config.CoquiModelName, ct);

        if (modelResult == null)
            return false;

        // Some models (e.g. jenny) are distributed as ZIP archives.
        // Extract them now so the TTS service can find the files.
        var extractedZips = CoquiTtsModelManager.ExtractModelZips(modelResult);
        if (extractedZips > 0)
            host.AddMessage($"[green]    ✓ Extracted {extractedZips} archive(s) for {modelResult}[/]");

        bool modelChanged = modelResult != config.CoquiModelName;

        // ── Phase 3: Download (prompt when not cached, regardless of model change) ──
        var downloadSucceeded = true;
        if (!CoquiTtsModelManager.IsModelCached(modelResult))
        {
            string promptText = modelChanged
                ? $"Model '{modelResult}' is not cached. Download now?"
                : $"Model '{modelResult}' (already configured) is not cached. Download now?";

            var shouldDownload = await host.PromptSelection(
                promptText,
                [new ConfigVariant("[green]Download now[/]", "download"),
                 new ConfigVariant("[yellow]Select without downloading[/]", "select"),
                 new ConfigVariant("[grey]Cancel[/]", "cancel")]);

            if (shouldDownload is not { Length: > 0 } || shouldDownload[0] is not ConfigVariant cv)
                return false;

            var choice = cv.Value;
            if (choice == "cancel")
                return false;

            if (choice == "download")
            {
                try
                {
                    await DownloadModelAsync(host, modelManager, modelResult, ct);
                }
                catch (OperationCanceledException)
                {
                    host.AddMessage("[yellow]  Download cancelled — model selected but not downloaded.[/]");
                    downloadSucceeded = false;
                }
                catch (Exception ex)
                {
                    host.AddMessage($"[red]  Download failed: {EscapeLine(ex.Message)}[/]");
                    host.AddMessage("[yellow]  Model selected but download failed. You can retry later.[/]");
                    downloadSucceeded = false;
                }
            }
            else
            {
                host.AddMessage("[grey]  Model selected without downloading. Use /reconfigure to download later.[/]");
                downloadSucceeded = false; // model not cached yet
            }
        }

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

    internal static async Task<string?> SelectModelAsync(
        IStreamShellHost host, CoquiTtsModelManager modelManager,
        AppConfig config, string? currentModel, CancellationToken ct)
    {
        var dataDir = config.CustomDataDir ?? config.DataDir;

        // Fetch live model list from Coqui TTS
        var allModels = await CoquiTtsModelManager.GetAvailableModelsAsync(
            host, dataDir, ct);

        if (allModels.Count == 0)
        {
            host.AddMessage("");
            host.AddMessage("[red]  ✗ No models available — live fetch from coqui/TTS failed.[/]");
            host.AddMessage("[grey]    Check the errors above and fix uv/Python issues.[/]");
            host.AddMessage("[grey]    Then re-run /reconfigure TTS to select a model.[/]");

            var errorDetail = CoquiTtsModelManager.LastFetchErrorDetail;
            var isUvBuildBroken = !string.IsNullOrEmpty(errorDetail) &&
                (errorDetail.Contains("Failed to build", StringComparison.Ordinal) ||
                 errorDetail.Contains("build_wheel", StringComparison.Ordinal) ||
                 errorDetail.Contains("build backend", StringComparison.Ordinal));
            if (isUvBuildBroken)
            {
                if (!string.IsNullOrEmpty(currentModel))
                    host.AddMessage($"[grey]    Current model: {currentModel}[/]");
                return currentModel;
            }
        }

        // Use Python to get actually-cached model paths (accurate)
        var cachedModels = new HashSet<string>(
            await CoquiTtsModelManager.GetCachedModelsAsync(host, dataDir, ct),
            StringComparer.Ordinal);

        string? result = null;
        var currentModelRef = currentModel;

        while (result == null)
        {
            ct.ThrowIfCancellationRequested();

            List<IVariantEntry> variants = BuildVariants(allModels, cachedModels, currentModelRef);
            variants.Add(new ConfigDecoration(""));
            variants.Add(new ConfigVariant("[grey]Cancel[/]", CancelSentinel));

            var selection = await host.PromptSelection(
                "Select Coqui TTS model, download, remove, or cancel:",
                variants.ToArray(), new SelectionInfo { Rows = 16});

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
                    bool removed = CoquiTtsModelManager.DeleteModel(modelName);
                    if (removed)
                    {
                        host.AddMessage($"[green]  ✓ Removed {modelName}[/]");
                        cachedModels = new HashSet<string>(
                            await CoquiTtsModelManager.GetCachedModelsAsync(host, dataDir, ct),
                            StringComparer.Ordinal);
                        allModels = await CoquiTtsModelManager.GetAvailableModelsAsync(
                            host, dataDir, ct);
                    }
                    else
                    {
                        host.AddMessage($"[grey]  Could not remove {modelName} (not found or partial cache).[/]");
                    }
                }
            }
        }

        return result;
    }

    // ── Download (bottom panel progress) ────────────────────────────

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
        catch (OperationCanceledException)
        {
            host.AddMessage("[yellow]  Download cancelled.[/]");
        }
        catch (Exception ex)
        {
            var errorText = ex.Message;
            host.AddMessage($"[red]  Download failed: {EscapeLine(errorText)}[/]");
        }
        finally
        {
            host.ResetBottomPanel();
        }
    }

    // ── Variant builder ─────────────────────────────────────────────

    private static string EscapeLine(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return Markup.Escape(text);
    }

    private static List<IVariantEntry> BuildVariants(
        IReadOnlyList<CoquiTtsModelInfo> allModels,
        HashSet<string> cachedModels, string? currentModel)
    {
        var variants = new List<IVariantEntry>(allModels.Count * 3 + 10);

        variants.Add(new ConfigDecoration(
            $"[bold green]── {allModels.Count} models from Coqui TTS ──[/]"));

        // ── Cached models ──
        foreach (var info in allModels)
        {
            if (!cachedModels.Contains(info.Name))
                continue;

            var isActive = info.Name == currentModel;
            var activeMarker = isActive ? " [cyan][[active]][/]" : "";
            variants.Add(new ConfigVariant(
                $"[green]✓ {info.Name}[/] [grey]({info.Description})[/]{activeMarker}",
                $"use:{info.Name}"));
        }

        // ── Not cached (downloadable) ──
        var notCached = allModels
            .Where(m => !cachedModels.Contains(m.Name))
            .ToList();

        if (notCached.Count > 0)
        {
            if (variants.Count > 1) // >1 because of the header
            {
                variants.Add(new ConfigDecoration(""));
                variants.Add(new ConfigDecoration("[bold cyan]── Available for download ──[/]"));
            }

            foreach (var info in notCached)
            {
                variants.Add(new ConfigVariant(
                    $"[grey]⬇ {info.Name} ({info.Description})[/]",
                    $"download:{info.Name}"));
            }
        }

        // ── Remove ──
        if (cachedModels.Count > 0)
        {
            variants.Add(new ConfigDecoration(""));
            variants.Add(new ConfigDecoration("[bold red]── Remove ──[/]"));
            foreach (var info in allModels)
            {
                if (!cachedModels.Contains(info.Name))
                    continue;
                variants.Add(new ConfigVariant(
                    $"[red]Remove: {info.Name}[/]",
                    $"remove:{info.Name}"));
            }
        }

        return variants;
    }
}

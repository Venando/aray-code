using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using OpenClawPTT.Transcriber;
using OpenClawPTT.TTS;
using OpenClawPTT.TTS.Providers;
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
            // Quick Python version check before triggering expensive operations
            host.AddMessage("[grey]    Checking Python environment...[/]");
            var versionError = await CoquiUvEnvironment.ValidatePythonVersionAsync(
                dataDir, ct);
            if (versionError != null)
            {
                host.AddMessage($"[red]    ✗ Python environment check failed: {versionError}[/]");
                host.AddMessage("[yellow]    Coqui TTS requires Python >=3.9 and <3.12.[/]");
                host.AddMessage("[grey]    uv can download the right Python automatically —[/]");
                host.AddMessage($"[grey]    add 'requires-python = \">=3.9,<3.12\"' to pyproject.toml.[/]");
                host.AddMessage("[grey]    Current pyproject.toml should already have this constraint.[/]");
                host.AddMessage("");
            }
            else
            {
                host.AddMessage("[green]    ✓ Python environment OK[/]");
            }
        }

        // ── Phase 2: Model selection ──
        var modelManager = new CoquiTtsModelManager(dataDir, host);
        var modelResult = await SelectModelAsync(
            host, modelManager, config, config.CoquiModelName, ct);

        if (modelResult == null)
            return false;

        bool modelChanged = modelResult != config.CoquiModelName;

        // ── Phase 3: Download (with user confirmation) ──
        var downloadSucceeded = true;
        if (!CoquiTtsModelManager.IsModelCached(modelResult))
        {
            var shouldDownload = await host.PromptSelection(
                $"Model '{modelResult}' is not cached. Download now?",
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
        // Fetch live model list from Coqui TTS (falls back to hardcoded)
        var allModels = await CoquiTtsModelManager.GetAvailableModelsAsync(
            host, config.CustomDataDir ?? config.DataDir, ct);

        var isFallback = CoquiTtsModelManager.IsUsingFallbackModels;

        // If the model list is from fallback, show a prominent warning before
        // presenting the model selection list so the user knows what they're seeing.
        if (isFallback)
        {
            host.AddMessage("");
            host.AddMessage("[bold yellow]  \u2500\u2500\u2500 Using Built-in (Offline) Model List \u2500\u2500\u2500[/]");
            host.AddMessage("[grey]    These are well-known Coqui TTS models hardcoded in OpenClawPTT.[/]");
            host.AddMessage("[grey]    Live models from coqui/TTS could not be fetched. See errors above.[/]");
            host.AddMessage("[grey]    Fix uv/Python issues and re-run to get the live model catalogue.[/]");
            host.AddMessage("");

            // If uv's dependency resolution itself broke (build error, not just timeout/missing),
            // there's no point in showing a model selection — the user needs to fix uv first.
            // Keep the current model and let them come back after fixing the environment.
            var errorDetail = CoquiTtsModelManager.LastFetchErrorDetail;
            var isUvBroken = !string.IsNullOrEmpty(errorDetail) &&
                             (errorDetail.Contains("uv exit=", StringComparison.Ordinal) ||
                              errorDetail.Contains("Failed to build", StringComparison.Ordinal) ||
                              errorDetail.Contains("build backend", StringComparison.Ordinal) ||
                              errorDetail.Contains("build_wheel", StringComparison.Ordinal));
            if (isUvBroken)
            {
                host.AddMessage("[yellow]  The uv environment could not be set up (build/dependency error).[/]");
                host.AddMessage("[grey]    Fix the issues above, then re-run /reconfigure TTS to select a model.[/]");
                if (!string.IsNullOrEmpty(currentModel))
                    host.AddMessage($"[grey]    Current model: {currentModel}[/]");
                return currentModel; // Keep current model, skip selection
            }
        }

        var cachedModels = new HashSet<string>(
            CoquiTtsModelManager.GetCachedModels(),
            StringComparer.Ordinal);

        string? result = null;
        var currentModelRef = currentModel; // captured for closure

        while (result == null)
        {
            ct.ThrowIfCancellationRequested();

            var variants = BuildVariants(allModels, cachedModels, currentModelRef, isFallback: isFallback);
            variants.Add(new ConfigVariant("", ""));
            variants.Add(new ConfigVariant("[grey]Cancel[/]", CancelSentinel));

            var selection = await host.PromptSelection(
                "Select Coqui TTS model, download, remove, or cancel:",
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
                    bool removed = CoquiTtsModelManager.DeleteModel(modelName);
                    if (removed)
                    {
                        host.AddMessage($"[green]  ✓ Removed {modelName}[/]");
                        cachedModels = new HashSet<string>(
                            CoquiTtsModelManager.GetCachedModels(),
                            StringComparer.Ordinal);
                        // Refresh live model list after removal
                        allModels = await CoquiTtsModelManager.GetAvailableModelsAsync(
                            host, config.CustomDataDir ?? config.DataDir, ct);
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
        return text.Replace("[", "[[").Replace("]", "]]");
    }

    private static List<IVariant> BuildVariants(
        IReadOnlyList<CoquiTtsModelInfo> allModels,
        HashSet<string> cachedModels, string? currentModel,
        bool isFallback = false)
    {
        var variants = new List<IVariant>(allModels.Count * 3 + 10);

        // ── Source indicator header ──
        if (isFallback)
        {
            variants.Add(new ConfigVariant(
                "[bold yellow]── Built-in list (uv not available) ──[/]",
                "__fallback_header__"));
        }
        else
        {
            variants.Add(new ConfigVariant(
                "[bold green]── Live from Coqui TTS ──[/]",
                "__live_header__"));
        }

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

        // ── Remove ──
        if (cachedModels.Count > 0)
        {
            variants.Add(new ConfigVariant("", ""));
            variants.Add(new ConfigVariant("[bold red]── Remove ──[/]", "__remove_header__"));
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

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
        // Ensure the pyproject.toml exists — uv needs it for dependency resolution
        var env = new CoquiUvEnvironment(
            config.CustomDataDir ?? config.DataDir,
            config.CoquiModelName ?? "tts_models/multilingual/mxtts/vits",
            config.CoquiModelPath,
            config.CoquiConfigPath,
            config.EspeakNgPath);
        env.EnsureProjectFiles();

        // Verify uv is available (checked in caller too, but guard here)
        if (!CoquiUvEnvironment.IsUvAvailable())
            return false;

        var modelManager = new CoquiTtsModelManager(config.CustomDataDir ?? config.DataDir, host);
        var modelResult = await SelectModelAsync(
            host, modelManager, config, config.CoquiModelName, ct);

        if (modelResult == null)
            return false;

        bool changed = false;
        if (modelResult != config.CoquiModelName)
        {
            config.CoquiModelName = modelResult;
            changed = true;
        }

        // Pre-download if not cached
        if (!CoquiTtsModelManager.IsModelCached(modelResult))
        {
            await DownloadModelAsync(host, modelManager, modelResult, ct);
        }

        if (changed)
        {
            config.SttProvider = null; // ensure TTS provider is CoquiUv
            host.AddMessage($"[green]  Model: {modelResult}[/]");
        }

        return changed;
    }

    internal static async Task<string?> SelectModelAsync(
        IStreamShellHost host, CoquiTtsModelManager modelManager,
        AppConfig config, string? currentModel, CancellationToken ct)
    {
        // Fetch live model list from Coqui TTS (falls back to hardcoded)
        var allModels = await CoquiTtsModelManager.GetAvailableModelsAsync(
            host, config.CustomDataDir ?? config.DataDir, ct);

        var cachedModels = new HashSet<string>(
            CoquiTtsModelManager.GetCachedModels(),
            StringComparer.Ordinal);

        string? result = null;
        var currentModelRef = currentModel; // captured for closure

        while (result == null)
        {
            ct.ThrowIfCancellationRequested();

            var variants = BuildVariants(allModels, cachedModels, currentModelRef);
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
            host.AddMessage($"[red]  Download failed: {ex.Message}[/]");
        }
        finally
        {
            host.ResetBottomPanel();
        }
    }

    // ── Variant builder ─────────────────────────────────────────────

    private static List<IVariant> BuildVariants(
        IReadOnlyList<CoquiTtsModelInfo> allModels,
        HashSet<string> cachedModels, string? currentModel)
    {
        var variants = new List<IVariant>(allModels.Count * 3 + 10);

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

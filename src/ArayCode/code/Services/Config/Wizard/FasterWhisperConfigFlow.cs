using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArayCode.Services;
using ArayCode.Services.Themes;
using ArayCode.Transcriber;
using StreamShell;

namespace ArayCode.ConfigWizard;

/// <summary>
/// Orchestrates the <c>faster-whisper</c> (uv-based) configuration flow:
/// <list type="number">
///   <item>Verifies <c>uv</c> is installed (shows install instructions if not)</item>
///   <item>Model selection — show cached models as "use", downloadable as "download"</item>
///   <item>Optional model pre-download (bottom-panel progress)</item>
/// </list>
/// </summary>
public sealed class FasterWhisperConfigFlow
{
    private const string CancelSentinel = "__cancel__";

    /// <summary>
    /// Runs the full faster-whisper configuration flow.
    /// Returns true if any config value was changed.
    /// </summary>
    public async Task<bool> RunAsync(
        IStreamShellHost host, AppConfig config, CancellationToken ct)
    {
        bool changed = false;

        // ── Step 0: Verify uv is installed ──
        if (!FasterWhisperEnvironment.IsUvAvailable())
        {
            host.AddMessage("");
            host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  ⚠ uv (Python package manager) is not installed.[/]");
            host.AddMessage($"[{ThemeProvider.Current.Tools.General.Muted}]    uv handles Python, packages, and dependencies automatically.[/]");
            host.AddMessage($"[{ThemeProvider.Current.Tools.General.Muted}]    Install: {FasterWhisperEnvironment.GetInstallInstructions()}[/]");
            host.AddMessage($"[{ThemeProvider.Current.Tools.General.Muted}]    Then re-run this configuration.[/]");
            host.AddMessage("");
            // Still allow continuing — user might install uv later
        }

        var env = new FasterWhisperEnvironment(config.CustomDataDir ?? config.DataDir);
        // Ensure pyproject.toml exists so user can manually run `uv run` if needed
        env.EnsureProjectFiles();

        var modelManager = new FasterWhisperModelManager(env, host);

        // ── Step 1: Model selection ──
        var modelResult = await SelectFasterWhisperModelAsync(
            host, modelManager, config.FasterWhisperModel, ct);

        if (modelResult == null)
            return changed; // cancelled

        if (modelResult != config.FasterWhisperModel)
        {
            config.FasterWhisperModel = modelResult;
            changed = true;
        }

        // ── Step 2: Pre-download model if not cached ──
        if (!FasterWhisperModelManager.IsModelCached(modelResult))
        {
            await WhisperDownloadProgress.DownloadFasterWhisperAsync(
                host, modelManager, modelResult, ct);
        }

        if (changed)
        {
            config.SttProvider = AppConfig.ProviderFasterWhisper;
            var uvStatus = FasterWhisperEnvironment.IsUvAvailable()
                ? $"[{ThemeProvider.Current.Tools.Messages.Success}]found[/]"
                : $"[{ThemeProvider.Current.Tools.Messages.Error}]not installed[/]";
            host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Success}]  uv: {uvStatus}[/]");
            host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Success}]  Model: {modelResult}[/]");
        }

        return changed;
    }

    // ── Model selection ─────────────────────────────────────────────

    internal static async Task<string?> SelectFasterWhisperModelAsync(
        IStreamShellHost host, FasterWhisperModelManager modelManager,
        string? currentModel, CancellationToken ct)
    {
        // Snapshot cached models once
        var cachedModels = new HashSet<string>(
            FasterWhisperModelManager.GetCachedModels(),
            StringComparer.Ordinal);

        string? result = null;

        while (result == null)
        {
            ct.ThrowIfCancellationRequested();

            var variants = BuildFasterWhisperVariants(cachedModels, currentModel);
            variants.Add(new ConfigVariant("", ""));
            variants.Add(new ConfigVariant($"[{ThemeProvider.Current.Tools.General.Muted}]Cancel[/]", CancelSentinel));

            var selection = await host.PromptSelection(
                "Select faster-whisper model, download, remove, or cancel:",
                variants.ToArray());

            if (selection is not { Length: > 0 } || selection[0] is not ConfigVariant cv)
                return result;

            var choice = cv.Value;
            if (choice == CancelSentinel)
                return result;

            if (choice.StartsWith("use:") || choice.StartsWith("download:"))
            {
                result = choice[(choice.StartsWith("use:") ? "use:" : "download:").Length..];
                // If user selected "download", we still return it — the caller handles the download
            }
            else if (choice.StartsWith("remove:"))
            {
                var modelName = choice["remove:".Length..];
                var confirm = await host.PromptSelection(
                    $"Remove model '{modelName}'?",
                    [new ConfigVariant($"[{ThemeProvider.Current.Tools.Messages.Error}]Yes, remove[/]", "yes"),
                     new ConfigVariant("Cancel", "no")]);

                if (confirm is { Length: > 0 } && confirm[0] is ConfigVariant cv2 && cv2.Value == "yes")
                {
                    bool removed = FasterWhisperModelManager.DeleteModel(modelName);
                    if (removed)
                    {
                        host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Success}]  ✓ Removed {modelName}[/]");
                        // Re-snapshot
                        cachedModels = new HashSet<string>(
                            FasterWhisperModelManager.GetCachedModels(),
                            StringComparer.Ordinal);
                    }
                }
            }
        }

        return result;
    }

    // ── Variant builder ─────────────────────────────────────────────

    private static List<IVariant> BuildFasterWhisperVariants(
        HashSet<string> cachedModels, string? currentModel)
    {
        var allModels = FasterWhisperModelManager.AvailableModels;
        var variants = new List<IVariant>(40);

        // ── Cached models ──
        foreach (var info in allModels)
        {
            if (!cachedModels.Contains(info.Name))
                continue;

            var isActive = info.Name == currentModel;
            var activeMarker = isActive ? $" [{ThemeProvider.Current.Tools.Messages.Highlight}][[active]][/]" : "";
            variants.Add(new ConfigVariant(
                $"[{ThemeProvider.Current.Tools.Messages.Success}]✓ {info.Name}[/] [{ThemeProvider.Current.Tools.General.Muted}]({info.Description})[/]{activeMarker}",
                $"use:{info.Name}"));
        }

        // ── Non-cached (downloadable) ──
        var notCached = allModels
            .Where(m => !cachedModels.Contains(m.Name))
            .ToList();

        if (notCached.Count > 0)
        {
            if (variants.Count > 0)
            {
                variants.Add(new ConfigVariant("", ""));
                variants.Add(new ConfigVariant($"[{ThemeProvider.Current.Tools.Panel.SectionHeader}]── Available for download ──[/]", "__header__"));
            }

            foreach (var info in notCached)
            {
                variants.Add(new ConfigVariant(
                    $"[{ThemeProvider.Current.Tools.General.Muted}]⬇ {info.Name} ({info.Description})[/]",
                    $"download:{info.Name}"));
            }
        }

        // ── Remove ──
        if (cachedModels.Count > 0)
        {
            variants.Add(new ConfigVariant("", ""));
            variants.Add(new ConfigVariant($"[bold {ThemeProvider.Current.Tools.Messages.Error}]── Remove ──[/]", "__remove_header__"));
            foreach (var info in allModels)
            {
                if (!cachedModels.Contains(info.Name))
                    continue;
                variants.Add(new ConfigVariant(
                    $"[{ThemeProvider.Current.Tools.Messages.Error}]Remove: {info.Name}[/]",
                    $"remove:{info.Name}"));
            }
        }

        return variants;
    }
}

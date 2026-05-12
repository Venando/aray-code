using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using OpenClawPTT.Transcriber;
using StreamShell;

namespace OpenClawPTT.ConfigWizard;

/// <summary>
/// Handles the whisper-specific configuration flow (binary type + model selection).
/// Unified flow for both Python openai-whisper and C++ whisper.cpp binary types.
/// </summary>
public sealed class WhisperConfigFlow
{
    private const string TypePython = "python";
    private const string TypeCpp = "cpp";
    private const string CancelSentinel = "__cancel__";
    private const string SpecifyPathSentinel = "__specify__";

    // ── Public entry point ───────────────────────────────────────────

    /// <summary>
    /// Runs the full whisper configuration flow: binary type selection, then model selection,
    /// and optional model download with bottom-panel progress.
    /// Returns true if any config value was changed.
    /// </summary>
    public async Task<bool> RunAsync(
        IStreamShellHost host, AppConfig config, CancellationToken ct)
    {
        var allBinaries = WhisperCppModelManager.FindAllWhisperBinaries();
        var modelManager = new WhisperCppModelManager(host, config.CustomDataDir ?? config.DataDir);

        // ── Step 1: Binary type selection ──
        var binaryType = await SelectBinaryTypeAsync(host, allBinaries, ct);
        if (binaryType == null)
        {
            // User cancelled — log nothing, return unchanged
            return false;
        }

        // Resolve the actual binary path for this type
        var resolvedPath = ResolveBinaryForType(binaryType.Value.Type, allBinaries);
        var resolvedBinary = resolvedPath != null
            ? new WhisperBinaryInfo(resolvedPath, binaryType.Value.Type)
            : null;

        // Update binary path if it changed
        bool changed = false;
        string resolvedBinaryPath;
        if (resolvedBinary != null)
        {
            resolvedBinaryPath = resolvedBinary.Path;
            if (resolvedBinaryPath != config.WhisperCppBinaryPath)
            {
                config.WhisperCppBinaryPath = resolvedBinaryPath;
                changed = true;
            }
        }
        else
        {
            // Type chosen but no binary found — let user specify path, or return
            var specifiedPath = await HandleMissingBinaryAsync(host, binaryType.Value.Type, ct);
            if (specifiedPath == null)
            {
                // User cancelled path specification
                return changed;
            }
            resolvedBinaryPath = specifiedPath;
            config.WhisperCppBinaryPath = specifiedPath;
            changed = true;
        }

        var isPython = binaryType.Value.Type == WhisperType.Python;

        // ── Step 2: Model selection (unified for Python and C++) ──
        var modelResult = await SelectModelAsync(host, modelManager, isPython, config.WhisperCppModel, ct);
        if (modelResult == null)
        {
            // User cancelled model selection — config may already have binary change
            if (changed)
                host.AddMessage($"[green]  Binary: {binaryType.Value.DisplayText}[/]");
            return changed;
        }

        if (modelResult != config.WhisperCppModel)
        {
            config.WhisperCppModel = modelResult;
            changed = true;
        }

        // ── Step 3: Download model if needed (C++ only — Python auto-downloads) ──
        if (!isPython && !modelManager.IsDownloaded(modelResult))
        {
            await DownloadModelWithProgressAsync(host, modelManager, modelResult, ct);
        }

        // ── Log final config via host message ──
        if (changed)
        {
            host.AddMessage($"[green]  Binary: {binaryType.Value.DisplayText}[/]");
            if (resolvedBinary != null)
                host.AddMessage($"[grey]  Path: {resolvedBinary.Path}[/]");
            host.AddMessage($"[green]  Model: {modelResult}[/]");
        }

        return changed;
    }

    // ── Binary type selection ────────────────────────────────────────

    private struct BinaryTypeOption
    {
        public string Id;
        public WhisperType Type;
        public string DisplayText;
        public bool IsAvailable;
        public string AvailabilityText;
    }

    /// <summary>
    /// Presents binary type (Python vs C++) as a PromptSelection.
    /// Both options are shown regardless of availability.
    /// Unavailable options display with [red] markup.
    /// Returns null if user cancels.
    /// </summary>
    private static async Task<BinaryTypeOption?> SelectBinaryTypeAsync(
        IStreamShellHost host, IReadOnlyList<WhisperBinaryInfo> allBinaries, CancellationToken ct)
    {
        var pythonBinaries = allBinaries.Where(b => b.Type == WhisperType.Python).ToList();
        var cppBinaries = allBinaries.Where(b => b.Type == WhisperType.Cpp).ToList();

        var hasPython = pythonBinaries.Count > 0;
        var hasCpp = cppBinaries.Count > 0;

        var options = new List<BinaryTypeOption>
        {
            new()
            {
                Id = TypePython,
                Type = WhisperType.Python,
                DisplayText = "Python (openai-whisper)",
                IsAvailable = hasPython,
                AvailabilityText = hasPython ? "[green]available[/]" : "[red]not found[/]",
            },
            new()
            {
                Id = TypeCpp,
                Type = WhisperType.Cpp,
                DisplayText = "C++ (whisper.cpp)",
                IsAvailable = hasCpp,
                AvailabilityText = hasCpp ? "[green]available[/]" : "[red]not found[/]",
            },
        };

        // Build variant list with availability markup
        var variants = new List<IVariant>();
        foreach (var opt in options)
        {
            var name = opt.IsAvailable
                ? $"{opt.DisplayText}  — {opt.AvailabilityText}"
                : $"[red]{opt.DisplayText}  — {opt.AvailabilityText}[/]";
            variants.Add(new ConfigVariant(name, opt.Id));
        }
        variants.Add(new ConfigVariant("", ""));
        variants.Add(new ConfigVariant("[grey]Cancel[/]", CancelSentinel));

        var result = await host.PromptSelection("Select whisper binary type:", variants.ToArray());
        if (result is not { Length: > 0 } || result[0] is not ConfigVariant cv)
            return null;

        if (cv.Value == CancelSentinel)
            return null;

        return options.FirstOrDefault(o => o.Id == cv.Value);
    }

    /// <summary>Resolve the first detected binary for a given type, or null if none found.</summary>
    private static string? ResolveBinaryForType(WhisperType type, IReadOnlyList<WhisperBinaryInfo> allBinaries)
    {
        return allBinaries.FirstOrDefault(b => b.Type == type)?.Path;
    }

    /// <summary>
    /// When a binary type is chosen but no binary was detected, let the user
    /// specify a path via the bottom panel, or cancel.
    /// Returns the path string, or null if cancelled.
    /// </summary>
    private static async Task<string?> HandleMissingBinaryAsync(
        IStreamShellHost host, WhisperType type, CancellationToken ct)
    {
        var typeName = type == WhisperType.Python ? "Python openai-whisper" : "C++ whisper.cpp";
        host.AddMessage($"[yellow]  No {typeName} binary detected on your system.[/]");

        // Use PromptSelection to ask what to do
        var variants = new IVariant[]
        {
            new ConfigVariant("[bold]Specify path manually...[/]", SpecifyPathSentinel),
            new ConfigVariant("", ""),
            new ConfigVariant("[grey]Cancel[/]", CancelSentinel),
        };

        var result = await host.PromptSelection($"No {typeName} binary found.", variants);
        if (result is not { Length: > 0 } || result[0] is not ConfigVariant cv)
            return null;

        if (cv.Value == CancelSentinel)
            return null;

        // "Specify path" — fall back to free text input
        // StreamShell doesn't have a free-text input in selections, so we show info
        host.AddMessage("");
        host.AddMessage($"[grey]  Install {typeName}:[/]");
        if (type == WhisperType.Python)
        {
            host.AddMessage("[grey]    pip install openai-whisper[/]");
        }
        else
        {
            host.AddMessage("[grey]    Build from: https://github.com/ggerganov/whisper.cpp[/]");
        }
        host.AddMessage("[grey]    Install the binary on PATH, then re-run this configuration.[/]");
        host.AddMessage("");

        return null;
    }

    // ── Model selection (unified) ────────────────────────────────────

    /// <summary>
    /// Presents available model options in PromptSelection.
    /// For Python: all models listed as selectable (auto-downloads on first use).
    /// For C++: shows downloaded models first, non-downloaded models can be downloaded,
    /// downloaded models can be removed.
    /// Returns the selected model name, or null if cancelled.
    /// </summary>
    private static async Task<string?> SelectModelAsync(
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
                // Python: all models shown as selectable (auto-download)
                foreach (var info in WhisperCppModelManager.AvailableModels)
                {
                    var isActive = info.Name == currentModel;
                    var name = isActive
                        ? $"[green]● {info.Name}[/] [cyan][[active]][/] [grey]({info.Description})[/]"
                        : $"[green]● {info.Name}[/] [grey]({info.Description})[/]";
                    variants.Add(new ConfigVariant(name, $"use:{info.Name}"));
                }
            }
            else
            {
                // C++: show downloaded and non-downloaded model groups
                var downloadedModels = modelManager.GetDownloadedModels();

                // Downloaded models — selectable
                if (downloadedModels.Count > 0)
                {
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
                }

                // Non-downloaded models — download option
                var notDownloaded = WhisperCppModelManager.AvailableModels
                    .Where(m => !downloadedModels.Contains(m.Name))
                    .ToList();

                if (notDownloaded.Count > 0)
                {
                    if (variants.Count > 0)
                    {
                        variants.Add(new ConfigVariant("", "")); // separator
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

            // Cancel option
            variants.Add(new ConfigVariant("", ""));
            variants.Add(new ConfigVariant("[grey]Cancel[/]", CancelSentinel));

            var promptText = isPython
                ? "Select model (auto-downloaded on first use):"
                : "Select model, download, or cancel:";

            var selection = await host.PromptSelection(promptText, variants.ToArray());
            if (selection is not { Length: > 0 } || selection[0] is not ConfigVariant cv)
                return result; // null = cancelled

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
                await DownloadModelWithProgressAsync(host, modelManager, modelName, ct);
                // After download, loop back to show updated model list
                // Select this model automatically
                result = modelName;
            }
        }

        return result;
    }

    // ── Model download with progress ─────────────────────────────────

    private static async Task DownloadModelWithProgressAsync(
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

    // ── Helpers ──────────────────────────────────────────────────────

}

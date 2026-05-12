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
/// Orchestrates the whisper-specific configuration flow: binary type selection,
/// model selection (delegated to <see cref="WhisperModelSelector"/>), and optional
/// model download (delegated to <see cref="WhisperDownloadProgress"/>).
/// Unified flow for both Python openai-whisper and C++ whisper.cpp binary types.
/// </summary>
public sealed class WhisperConfigFlow
{
    private const string TypePython = "python";
    private const string TypeCpp = "cpp";
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

        // ── Step 1: Binary type selection ──
        var binaryType = await SelectBinaryTypeAsync(host, allBinaries, ct);
        if (binaryType == null)
            return false;

        var resolvedPath = ResolveBinaryForType(binaryType.Value.Type, allBinaries);
        var resolvedBinary = resolvedPath != null
            ? new WhisperBinaryInfo(resolvedPath, binaryType.Value.Type)
            : null;

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
            await HandleMissingBinaryAsync(host, binaryType.Value.Type, ct);
            return changed;
        }

        var isPython = binaryType.Value.Type == WhisperType.Python;

        // ── Step 2: Model selection ──
        var modelResult = await WhisperModelSelector.SelectModelAsync(
            host, modelManager, isPython, config.WhisperCppModel, ct);
        if (modelResult == null)
        {
            if (changed)
                host.AddMessage($"[green]  Binary: {binaryType.Value.DisplayText}[/]");
            return changed;
        }

        if (modelResult != config.WhisperCppModel)
        {
            config.WhisperCppModel = modelResult;
            changed = true;
        }

        // ── Step 3: Download model if needed (both Python and C++) ──
        if (isPython)
        {
            if (!WhisperCppModelManager.IsPythonModelCached(modelResult))
            {
                await WhisperDownloadProgress.DownloadPythonAsync(
                    host, resolvedBinaryPath, modelResult, ct);
            }
        }
        else
        {
            if (!modelManager.IsDownloaded(modelResult))
            {
                await WhisperDownloadProgress.DownloadCppAsync(
                    host, modelManager, modelResult, ct);
            }
        }

        // ── Log final config ──
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

    internal struct BinaryTypeOption
    {
        public string Id;
        public WhisperType Type;
        public string DisplayText;
        public bool IsAvailable;
        public string AvailabilityText;
    }

    /// <summary>
    /// Presents binary type (Python vs C++) as a PromptSelection.
    /// Both options shown regardless of availability; unavailable ones use [red] markup.
    /// </summary>
    private static async Task<BinaryTypeOption?> SelectBinaryTypeAsync(
        IStreamShellHost host, IReadOnlyList<WhisperBinaryInfo> allBinaries, CancellationToken ct)
    {
        var hasPython = allBinaries.Any(b => b.Type == WhisperType.Python);
        var hasCpp = allBinaries.Any(b => b.Type == WhisperType.Cpp);

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
        => allBinaries.FirstOrDefault(b => b.Type == type)?.Path;

    /// <summary>
    /// Shows install instructions when a binary type was chosen but no binary detected.
    /// </summary>
    private static async Task HandleMissingBinaryAsync(
        IStreamShellHost host, WhisperType type, CancellationToken ct)
    {
        var typeName = type == WhisperType.Python ? "Python openai-whisper" : "C++ whisper.cpp";

        host.AddMessage("");
        host.AddMessage($"[yellow]  ⚠ No {typeName} binary detected on your system.[/]");
        host.AddMessage("");
        host.AddMessage($"[grey]  Install {typeName}:[/]");
        host.AddMessage(type == WhisperType.Python
            ? "[grey]    pip install openai-whisper[/]"
            : "[grey]    Build from: https://github.com/ggerganov/whisper.cpp[/]");
        host.AddMessage("[grey]    Add it to PATH, then re-run this configuration.[/]");
        host.AddMessage("");

        var variants = new IVariant[] { new ConfigVariant("[grey]Cancel[/]", CancelSentinel) };
        _ = await host.PromptSelection($"{typeName} not installed.", variants);
    }
}

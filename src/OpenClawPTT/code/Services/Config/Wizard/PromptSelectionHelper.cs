using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using StreamShell;

namespace OpenClawPTT.ConfigWizard;

/// <summary>
/// Reusable PromptSelection helpers for bool, enum, and string selections.
/// StreamShell does not support uncancellable selections — when a selection must be made,
/// the helper loops until the user picks something.
/// </summary>
public static class PromptSelectionHelper
{
    private const string BackSentinel = "__back__";
    public const string CancelSentinel = "__cancel__";

    private static async Task<ConfigVariant?> PromptLoopAsync(
        IStreamShellHost host,
        string title,
        IVariant[] variants,
        bool allowCancel,
        CancellationToken ct)
    {
        var result = await host.PromptSelection(title, variants, new SelectionInfo()
        {
            Max = 1,
            Min = 1,
            PreventCancel = !allowCancel
        });

        if (result is { Length: > 0 } && result[0] is ConfigVariant cv)
            return cv;

        return null;
    }

    private static string HighlightDefault(string name, bool isDefault) =>
        isDefault ? $"[underline]{name}[/]" : name;

    // ── Bool ─────────────────────────────────────────────────────────

    /// <summary>Prompt Yes/No. If cancelled and allowCancel is false, re-prompts.</summary>
    public static async Task<bool?> PromptBoolAsync(
        IStreamShellHost host,
        string title,
        bool? defaultValue = null,
        bool allowCancel = false,
        string yesPrompt = "Yes",
        string noPrompt = "No",
        bool yesFirst = true,
        CancellationToken cancellationToken = default)
    {
        PromptHelper.PrintPrePromptMessage(host);
        var yesVariant = new ConfigVariant(HighlightDefault(yesPrompt, defaultValue ?? false), "yes");
        var noVariant = new ConfigVariant(HighlightDefault(noPrompt, !defaultValue ?? false), "no");

        IVariant[] variants = yesFirst ? [yesVariant, noVariant] : [noVariant, yesVariant];
        var cv = await PromptLoopAsync(host, title, variants, allowCancel, cancellationToken);
        return cv == null ? null : cv?.Value == "yes";
    }


    /// <summary>Prompt Skip/Yes. If cancelled and allowCancel is false, re-prompts.</summary>
    public static async Task<bool?> PromptSkipOrProceedAsync(
        IStreamShellHost host,
        string title,
        bool? defaultValue = null,
        bool allowCancel = false,
        CancellationToken cancellationToken = default)
    {
        return await PromptBoolAsync(host, title, defaultValue, allowCancel, "Yes", "Skip", false, cancellationToken);
    }

    // ── Enum ─────────────────────────────────────────────────────────

    /// <summary>Prompt selection from enum values. If cancelled and allowCancel is false, re-prompts.</summary>
    public static async Task<T?> PromptEnumAsync<T>(
        IStreamShellHost host,
        string title,
        T defaultValue,
        bool allowCancel = false,
        CancellationToken cancellationToken = default) where T : struct, Enum
    {
        PromptHelper.PrintPrePromptMessage(host);

        var variants = Enum.GetValues<T>().Select(v =>
        {
            var name = GetEnumDisplayName(v);
            return new ConfigVariant(HighlightDefault(name, EqualityComparer<T>.Default.Equals(v, defaultValue)), v.ToString());
        }).ToArray<IVariant>();

        var cv = await PromptLoopAsync(host, title, variants, allowCancel, cancellationToken);

        return cv == null ? null : Enum.Parse<T>(cv.Value);
    }

    /// <summary>Prompt enum selection with a "Back" option. Returns null if Back is chosen.</summary>
    public static async Task<T?> PromptEnumWithBackAsync<T>(
        IStreamShellHost host,
        string title,
        T defaultValue,
        CancellationToken cancellationToken = default) where T : struct, Enum
    {
        var variants = Enum.GetValues<T>().Select(v =>
        {
            var name = GetEnumDisplayName(v);
            return new ConfigVariant(HighlightDefault(name, EqualityComparer<T>.Default.Equals(v, defaultValue)), v.ToString());
        }).ToArray<IVariant>();

        var cv = await PromptWithBackLoopAsync(host, title, variants, cancellationToken);
        if (cv == null)
            return null;
        return Enum.Parse<T>(cv.Value);
    }

    // ── String from options ──────────────────────────────────────────

    /// <summary>Prompt selection from string options. If cancelled and allowCancel is false, re-prompts.</summary>
    public static async Task<string?> PromptStringAsync(
        IStreamShellHost host,
        string title,
        (string Name, string Value)[] options,
        string? defaultValue = null,
        bool allowCancel = false,
        CancellationToken cancellationToken = default)
    {
        PromptHelper.PrintPrePromptMessage(host);

        var variants = options.Select(o =>
            new ConfigVariant(HighlightDefault(o.Name, o.Value == defaultValue), o.Value)
        ).ToArray<IVariant>();

        var cv = await PromptLoopAsync(host, title, variants, allowCancel, cancellationToken);
        return cv?.Value;
    }

    /// <summary>Prompt string selection with a "Back" option. Returns null if Back is chosen.</summary>
    public static async Task<string?> PromptStringWithBackAsync(
        IStreamShellHost host,
        string title,
        (string Name, string Value)[] options,
        string? defaultValue = null,
        CancellationToken cancellationToken = default)
    {
        var variants = options.Select(o =>
            new ConfigVariant(HighlightDefault(o.Name, o.Value == defaultValue), o.Value)
        ).ToArray<IVariant>();

        var cv = await PromptWithBackLoopAsync(host, title, variants, cancellationToken);
        return cv?.Value;
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static async Task<ConfigVariant?> PromptWithBackLoopAsync(
        IStreamShellHost host,
        string title,
        IVariant[] variants,
        CancellationToken ct)
    {
        PromptHelper.PrintPrePromptMessage(host);

        var backVariants = new IVariant[] { new ConfigVariant("[underline]Back[/]", BackSentinel) }
            .Concat(variants).ToArray();

        var result = await host.PromptSelection(title, backVariants);
        if (result is { Length: > 0 } && result[0] is ConfigVariant cv)
            return cv;
            
        return null;
    }

    private static string GetEnumDisplayName<T>(T value) where T : struct, Enum
    {
        return value.ToString();
    }
}

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

    // ── Bool ─────────────────────────────────────────────────────────

    /// <summary>Prompt Yes/No. If cancelled and allowCancel is false, re-prompts.</summary>
    public static async Task<bool> PromptBoolAsync(
        IStreamShellHost host,
        string title,
        bool defaultValue = true,
        bool allowCancel = false,
        CancellationToken ct = default)
    {
        var yesVariant = new ConfigVariant(defaultValue ? "[green]Yes[/]" : "Yes", "yes");
        var noVariant = new ConfigVariant(!defaultValue ? "[green]No[/]" : "No", "no");
        var variants = new IVariant[] { yesVariant, noVariant };

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = await host.PromptSelection(title, variants);
                if (result is { Length: > 0 })
                    return ((ConfigVariant)result[0]).Value == "yes";
            }
            catch (OperationCanceledException)
            {
                if (allowCancel)
                    throw;
            }

            if (allowCancel)
                throw new OperationCanceledException();
            // Loop — force a selection
        }
    }

    // ── Enum ─────────────────────────────────────────────────────────

    /// <summary>Prompt selection from enum values. If cancelled and allowCancel is false, re-prompts.</summary>
    public static async Task<T> PromptEnumAsync<T>(
        IStreamShellHost host,
        string title,
        T defaultValue,
        bool allowCancel = false,
        CancellationToken ct = default) where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        var variantList = values.Select(v =>
        {
            var name = GetEnumDisplayName(v);
            var isDefault = EqualityComparer<T>.Default.Equals(v, defaultValue);
            return new ConfigVariant(isDefault ? $"[green]{name}[/]" : name, v.ToString());
        }).ToList();
        var variants = variantList.Cast<IVariant>().ToArray();

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = await host.PromptSelection(title, variants);
                if (result is { Length: > 0 } && Enum.TryParse<T>(((ConfigVariant)result[0]).Value, out var parsed))
                    return parsed;
            }
            catch (OperationCanceledException)
            {
                if (allowCancel)
                    throw;
            }

            if (allowCancel)
                throw new OperationCanceledException();
        }
    }

    /// <summary>Prompt enum selection with a "Back" option. Returns null if Back is chosen.</summary>
    public static async Task<T?> PromptEnumWithBackAsync<T>(
        IStreamShellHost host,
        string title,
        T defaultValue,
        CancellationToken ct = default) where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        var variants = new System.Collections.Generic.List<IVariant>
        {
            new ConfigVariant("[grey]← Back[/]", BackSentinel)
        };
        variants.AddRange(values.Select(v =>
        {
            var name = GetEnumDisplayName(v);
            var isDefault = EqualityComparer<T>.Default.Equals(v, defaultValue);
            return new ConfigVariant(isDefault ? $"[green]{name}[/]" : name, v.ToString());
        }));

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = await host.PromptSelection(title, variants.ToArray());
                if (result is not { Length: > 0 })
                    continue; // cancelled — force selection

                var value = ((ConfigVariant)result[0]).Value;
                if (value == BackSentinel)
                    return null;

                if (Enum.TryParse<T>(value, out var parsed))
                    return parsed;
            }
            catch (OperationCanceledException)
            {
                // Force selection — loop and re-prompt
            }
        }
    }

    // ── String from options ──────────────────────────────────────────

    /// <summary>Prompt selection from string options. If cancelled and allowCancel is false, re-prompts.</summary>
    public static async Task<string> PromptStringAsync(
        IStreamShellHost host,
        string title,
        (string Name, string Value)[] options,
        string defaultValue,
        bool allowCancel = false,
        CancellationToken ct = default)
    {
        var variantList = options.Select(o =>
        {
            var isDefault = o.Value == defaultValue;
            return new ConfigVariant(isDefault ? $"[green]{o.Name}[/]" : o.Name, o.Value);
        }).ToList();
        var variants = variantList.Cast<IVariant>().ToArray();

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = await host.PromptSelection(title, variants);
                if (result is { Length: > 0 })
                    return ((ConfigVariant)result[0]).Value;
            }
            catch (OperationCanceledException)
            {
                if (allowCancel)
                    throw;
            }

            if (allowCancel)
                throw new OperationCanceledException();
        }
    }

    /// <summary>Prompt string selection with a "Back" option. Returns null if Back is chosen.</summary>
    public static async Task<string?> PromptStringWithBackAsync(
        IStreamShellHost host,
        string title,
        (string Name, string Value)[] options,
        string defaultValue,
        CancellationToken ct = default)
    {
        var variants = new System.Collections.Generic.List<IVariant>
        {
            new ConfigVariant("[grey]← Back[/]", BackSentinel)
        };
        variants.AddRange(options.Select(o =>
        {
            var isDefault = o.Value == defaultValue;
            return new ConfigVariant(isDefault ? $"[green]{o.Name}[/]" : o.Name, o.Value);
        }));

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = await host.PromptSelection(title, variants.ToArray());
                if (result is not { Length: > 0 })
                    continue; // cancelled — force selection

                var value = ((ConfigVariant)result[0]).Value;
                if (value == BackSentinel)
                    return null;

                return value;
            }
            catch (OperationCanceledException)
            {
                // Force selection — loop and re-prompt
            }
        }
    }

    // ── Helper ───────────────────────────────────────────────────────

    private static string GetEnumDisplayName<T>(T value) where T : struct, Enum
    {
        // Use the enum value name directly. For nicer names, consider adding a display-name attribute later.
        return value.ToString();
    }
}

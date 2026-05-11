using System;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using Spectre.Console;
using StreamShell;

namespace OpenClawPTT.ConfigWizard;

/// <summary>
/// Shared helper for free-text prompts during the configuration wizard.
/// Subscribes to <see cref="IStreamShellHost.UserInputSubmitted"/>, validates input,
/// and returns the result. Handles cancellation and exceptions safely.
/// </summary>
public static class PromptTextHelper
{
    /// <summary>
    /// Prompts the user for free-text input via StreamShell.
    /// </summary>
    public static async Task<string?> PromptAsync(
        IStreamShellHost host,
        string description,
        string defaultValue,
        Func<string, bool> validate,
        string? validationHint,
        CancellationToken ct,
        bool isSecret = false,
        bool allowEmpty = false,
        bool allowClear = false)
    {
        var tcs = new TaskCompletionSource<string?>();

        void OnInput(StreamShell.UserInputSubmittedEventArgs e)
        {
            try
            {
                var input = (e.TextWithoutAttachments ?? e.RawOutput ?? "").Trim();

                if (allowClear && input == "--")
                {
                    host.AddMessage("[green]  ✓ (cleared)[/]");
                    tcs.TrySetResult("");
                    return;
                }

                if (string.IsNullOrEmpty(input))
                {
                    if (allowEmpty)
                    {
                        tcs.TrySetResult("");
                        return;
                    }
                    tcs.TrySetResult(defaultValue);
                    return;
                }

                if (!validate(input))
                {
                    host.AddMessage($"[red]  ✗ Invalid value.{(validationHint != null ? " " + validationHint : "")}[/]");
                    SendPrompt(host, description, defaultValue, isSecret);
                    return;
                }

                var displayValue = isSecret ? MaskSecret(input) : input;
                host.AddMessage($"[green]  ✓ {Markup.Escape(displayValue)}[/]");
                tcs.TrySetResult(input);
            }
            catch (Exception ex)
            {
                // Don't let exceptions in the event handler crash StreamShell's dispatch loop.
                // Fault the TCS so the awaiting caller sees the error.
                tcs.TrySetException(ex);
            }
        }

        host.UserInputSubmitted += OnInput;
        try
        {
            SendPrompt(host, description, defaultValue, isSecret);
            using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
            return await tcs.Task;
        }
        finally
        {
            host.UserInputSubmitted -= OnInput;
        }
    }

    /// <summary>Prompts for an integer value within a range.</summary>
    public static async Task<int?> PromptIntAsync(
        IStreamShellHost host,
        string description,
        int defaultValue,
        int min,
        int max,
        CancellationToken ct)
    {
        var result = await PromptAsync(host, description,
            defaultValue.ToString(),
            v => int.TryParse(v, out var n) && n >= min && n <= max,
            $"Expected a number between {min} and {max}",
            ct);
        return result != null && int.TryParse(result, out var n) ? n : null;
    }

    /// <summary>Prompts for a double value within a range.</summary>
    public static async Task<double?> PromptDoubleAsync(
        IStreamShellHost host,
        string description,
        double defaultValue,
        double min,
        double max,
        CancellationToken ct)
    {
        var result = await PromptAsync(host, description,
            defaultValue.ToString("F2"),
            v => double.TryParse(v, out var d) && d >= min && d <= max,
            $"Expected a number between {min} and {max}",
            ct);
        return result != null && double.TryParse(result, out var d) ? d : null;
    }

    private static void SendPrompt(IStreamShellHost host, string description, string defaultValue, bool isSecret)
    {
        host.AddMessage($"[cyan2]▸ {Markup.Escape(description)}[/]");
        var displayDefault = isSecret ? MaskSecret(defaultValue ?? "") : defaultValue;
        if (!string.IsNullOrEmpty(displayDefault))
            host.AddMessage($"  [grey](current: {Markup.Escape(displayDefault)}, press Enter to keep)[/]");
    }

    private static string MaskSecret(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "(not set)";
        if (value.Length <= 4)
            return new string('*', value.Length);
        return value[..4] + new string('*', Math.Min(value.Length - 4, 12));
    }
}

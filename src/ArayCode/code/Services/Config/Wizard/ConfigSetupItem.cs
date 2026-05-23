using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArayCode.Services;
using StreamShell;

namespace ArayCode.ConfigWizard;

/// <summary>
/// Universal descriptor for a configurable field in AppConfig.
/// Each item knows its title, which property it targets, and how to prompt the user.
/// Use the static factory methods to create instances for each field type.
/// </summary>
public sealed class ConfigSetupItem
{
    /// <summary>Prompt text shown to the user.</summary>
    public string Title { get; init; }

    /// <summary>Property name on <see cref="AppConfig"/>.</summary>
    public string FieldName { get; init; }

    /// <summary>Optional short description shown below the prompt.</summary>
    public string? Description { get; init; }

    /// <summary>True when the field value should be masked in settings display.</summary>
    public bool IsSecret { get; init; }

    /// <summary>The prompt-and-apply delegate created by the factory method.</summary>
    internal Func<IStreamShellHost, AppConfig, bool, CancellationToken, Task<bool>> PromptAndApplyAsync { get; init; }

    private ConfigSetupItem(string title, string fieldName,
        Func<IStreamShellHost, AppConfig, bool, CancellationToken, Task<bool>> handler)
    {
        Title = title;
        FieldName = fieldName;
        PromptAndApplyAsync = handler;
    }

    /// <summary>Runs the prompt for this item and applies the result to config.</summary>
    public Task<bool> RunAsync(IStreamShellHost host, AppConfig config, bool isInitialSetup, CancellationToken ct)
        => PromptAndApplyAsync(host, config, isInitialSetup, ct);

    // ── Factory methods ──────────────────────────────────────────────

    /// <summary>Configures a free-text string field.</summary>
    /// <param name="transform">Optional post-processor applied to the submitted value before storing
    /// (e.g. prefix normalization). Applied before the nullable-empty check, so it can alter the value
    /// that is compared against the current config value.</param>
    public static ConfigSetupItem ForString(
        string title,
        string fieldName,
        Func<string, bool>? validator = null,
        string? validationHint = null,
        bool isSecret = false,
        bool isEmptyToDefault = false,
        bool allowClear = false,
        string? description = null,
        Func<string, string>? transform = null)
    {
        return new ConfigSetupItem(title, fieldName, (host, config, _, ct) =>
            PromptStringAsync(host, title, config, fieldName,
                validator ?? (_ => true), validationHint, ct,
                isSecret, isEmptyToDefault, allowClear, transform))
        { IsSecret = isSecret };
    }

    /// <summary>Configures a Yes/No boolean field.</summary>
    public static ConfigSetupItem ForBool(
        string title,
        string fieldName,
        string? description = null)
    {
        return new ConfigSetupItem(title, fieldName, (host, config, _, ct) =>
            PromptBoolAsync(host, title, config, fieldName, ct));
    }

    /// <summary>Configures an integer field within a range.</summary>
    public static ConfigSetupItem ForInt(
        string title,
        string fieldName,
        int min,
        int max,
        string? description = null)
    {
        return new ConfigSetupItem(title, fieldName, (host, config, _, ct) =>
            PromptIntAsync(host, title, config, fieldName, min, max, ct));
    }

    /// <summary>Configures a double field within a range.</summary>
    public static ConfigSetupItem ForDouble(
        string title,
        string fieldName,
        double min,
        double max,
        string? description = null)
    {
        return new ConfigSetupItem(title, fieldName, (host, config, _, ct) =>
            PromptDoubleAsync(host, title, config, fieldName, min, max, ct));
    }

    /// <summary>Configures an enum field (prompts via selection).</summary>
    public static ConfigSetupItem ForEnum<T>(
        string title,
        string fieldName,
        string? description = null)
        where T : struct, Enum
    {
        return new ConfigSetupItem(title, fieldName, (host, config, _, ct) =>
            PromptEnumAsync<T>(host, title, config, fieldName, ct));
    }

    /// <summary>Configures a field via named options (selection prompt).</summary>
    public static ConfigSetupItem ForSelection(
        string title,
        string fieldName,
        (string Name, string Value)[] options,
        string? description = null)
    {
        return new ConfigSetupItem(title, fieldName, (host, config, _, ct) =>
            PromptSelectionAsync(host, title, config, fieldName, options, ct));
    }

    /// <summary>Configures a field via named options with a "Back" option. Returns null (no change) on Back.</summary>
    public static ConfigSetupItem ForSelectionWithBack(
        string title,
        string fieldName,
        (string Name, string Value)[] options,
        string? description = null)
    {
        return new ConfigSetupItem(title, fieldName, (host, config, _, ct) =>
            PromptSelectionWithBackAsync(host, title, config, fieldName, options, ct));
    }

    // ── Reflection helpers ───────────────────────────────────────────

    private static T? GetValue<T>(AppConfig config, string fieldName)
    {
        var prop = typeof(AppConfig).GetProperty(fieldName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop == null)
            throw new ArgumentException($"AppConfig has no property '{fieldName}'");
        return (T?)prop.GetValue(config);
    }

    private static void SetValue(AppConfig config, string fieldName, object? value)
    {
        var prop = typeof(AppConfig).GetProperty(fieldName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop == null)
            throw new ArgumentException($"AppConfig has no property '{fieldName}'");

        // If the property is an enum and value is a string, parse it
        if (prop.PropertyType.IsEnum && value is string strValue)
            value = Enum.Parse(prop.PropertyType, strValue);

        prop.SetValue(config, value);
    }

    // ── Display helpers ────────────────────────────────────────────

    /// <summary>Returns the current value of the field from config, masked if this is a secret field.</summary>
    public string GetDisplayValue(AppConfig config)
    {
        var prop = typeof(AppConfig).GetProperty(FieldName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        var raw = prop?.GetValue(config);
        var text = raw?.ToString() ?? "";
        return IsSecret ? MaskSecret(text) : text;
    }

    /// <summary>Masks a secret value for display, showing first 4 chars followed by asterisks.</summary>
    public static string MaskSecret(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "(not set)";
        if (value.Length <= 4)
            return new string('*', value.Length);
        return value[..4] + new string('*', Math.Min(value.Length - 4, 12));
    }

    // ── Prompt implementations ───────────────────────────────────────

    private static async Task<bool> PromptStringAsync(
        IStreamShellHost host, string title, AppConfig config, string fieldName,
        Func<string, bool> validator, string? validationHint, CancellationToken ct,
        bool isSecret, bool isEmptyToDefault, bool allowClear,
        Func<string, string>? transform = null)
    {
        var current = GetValue<string?>(config, fieldName) ?? "";
        var result = await PromptTextHelper.PromptAsync(host, title, current,
            validator, validationHint, ct, isSecret, isEmptyToDefault, allowClear);
        if (result == null)
            return false;

        // Apply value transform (e.g. prefix normalization) before comparison
        var transformed = transform != null ? transform(result) : result;

        // For nullable strings, convert empty to null if needed
        var prop = typeof(AppConfig).GetProperty(fieldName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        bool isNullableString = prop?.PropertyType == typeof(string) && prop.GetCustomAttribute<System.Runtime.CompilerServices.NullableAttribute>()?.NullableFlags[0] == 2
                                || Nullable.GetUnderlyingType(prop?.PropertyType!) != null;

        object? newValue = transformed;
        if (isNullableString && string.IsNullOrEmpty(transformed))
            newValue = null;

        if (!Equals(newValue, current))
        {
            SetValue(config, fieldName, newValue);
            return true;
        }
        return false;
    }

    private static async Task<bool> PromptBoolAsync(
        IStreamShellHost host, string title, AppConfig config, string fieldName, CancellationToken ct)
    {
        var current = GetValue<bool>(config, fieldName);
        var result = await PromptSelectionHelper.PromptBoolAsync(host, title, current,
            allowCancel: false, cancellationToken: ct);
        if (result.HasValue && result.Value != current)
        {
            SetValue(config, fieldName, result.Value);
            return true;
        }
        return false;
    }

    private static async Task<bool> PromptIntAsync(
        IStreamShellHost host, string title, AppConfig config, string fieldName,
        int min, int max, CancellationToken ct)
    {
        var current = GetValue<int>(config, fieldName);
        var result = await PromptTextHelper.PromptIntAsync(host, title, current, min, max, ct);
        if (result.HasValue && result.Value != current)
        {
            SetValue(config, fieldName, result.Value);
            return true;
        }
        return false;
    }

    private static async Task<bool> PromptDoubleAsync(
        IStreamShellHost host, string title, AppConfig config, string fieldName,
        double min, double max, CancellationToken ct)
    {
        var current = GetValue<double>(config, fieldName);
        var result = await PromptTextHelper.PromptDoubleAsync(host, title, current, min, max, ct);
        if (result.HasValue && Math.Abs(result.Value - current) > 0.0001)
        {
            SetValue(config, fieldName, result.Value);
            return true;
        }
        return false;
    }

    private static async Task<bool> PromptEnumAsync<T>(
        IStreamShellHost host, string title, AppConfig config, string fieldName, CancellationToken ct)
        where T : struct, Enum
    {
        var current = GetValue<T>(config, fieldName);
        var result = await PromptSelectionHelper.PromptEnumAsync<T>(host, title, current,
            allowCancel: false, cancellationToken: ct);
        if (result.HasValue && !EqualityComparer<T>.Default.Equals(result.Value, current))
        {
            // Enum properties might be stored as string or the enum itself
            // Try to set the native enum type
            var prop = typeof(AppConfig).GetProperty(fieldName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop?.PropertyType == typeof(T))
            {
                SetValue(config, fieldName, result.Value);
            }
            else
            {
                // Store as string
                SetValue(config, fieldName, result.Value.ToString());
            }
            return true;
        }
        return false;
    }

    private static async Task<bool> PromptSelectionAsync(
        IStreamShellHost host, string title, AppConfig config, string fieldName,
        (string Name, string Value)[] options, CancellationToken ct)
    {
        var current = GetCurrentString(config, fieldName);
        var result = await PromptSelectionHelper.PromptStringAsync(host, title, options, current,
            allowCancel: false, cancellationToken: ct);
        if (result != null && result != current)
        {
            SetValue(config, fieldName, result);
            return true;
        }
        return false;
    }

    private static async Task<bool> PromptSelectionWithBackAsync(
        IStreamShellHost host, string title, AppConfig config, string fieldName,
        (string Name, string Value)[] options, CancellationToken ct)
    {
        var current = GetCurrentString(config, fieldName);
        var result = await PromptSelectionHelper.PromptStringAsync(host, title, options, current, cancellationToken: ct);
        if (result != null && result != current)
        {
            SetValue(config, fieldName, result);
            return true;
        }
        return false;
    }

    /// <summary>Reads a property value as a string, regardless of whether it's a string, enum, or other type.</summary>
    private static string GetCurrentString(AppConfig config, string fieldName)
    {
        var prop = typeof(AppConfig).GetProperty(fieldName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop == null)
            return "";
        var raw = prop.GetValue(config);
        return raw?.ToString() ?? "";
    }
}

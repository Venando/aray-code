using System.Linq;
using ArayCode.Services.Themes;
using Spectre.Console;

namespace ArayCode.Services.Commands;

/// <summary>Native command: /appconfig — gets or sets any app config value.</summary>
public sealed class AppConfigCommand : ICommand
{
    private readonly IStreamShellHost _host;
    private readonly Func<AppConfig> _getConfig;
    private readonly IConfigurationService _configService;

    public string Name => "appconfig";
    public string Description => "<key> [value] Get or set app config value";
    public CommandSource Source => CommandSource.Native;
    public ShellCommandType Type => ShellCommandType.Configuration;
    public string[]? Suggestions { get; }

    public AppConfigCommand(
        IStreamShellHost host,
        Func<AppConfig> getConfig,
        IConfigurationService configService)
    {
        _host = host;
        _getConfig = getConfig;
        _configService = configService;
        Suggestions = OpenClawCommandSuggestions.GetAppConfigSuggestions();
    }

    public Task ExecuteAsync(string[] args, Dictionary<string, string> namedArgs, CancellationToken ct = default)
    {
        if (args.Length == 0)
        {
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  Usage: /appconfig <key> [value][/]");
            _host.AddMessage($"[{ThemeProvider.Current.Tools.General.Muted}]  Examples:[/]");
            _host.AddMessage("    /appconfig DirectLlmUrl           (show current value)");
            _host.AddMessage("    /appconfig DirectLlmUrl http://... (set new value)");
            return Task.CompletedTask;
        }

        var key = args[0];
        var value = args.Length > 1 ? string.Join(" ", args.Skip(1)) : null;

        var property = typeof(AppConfig).GetProperty(key,
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.IgnoreCase);

        if (property == null)
        {
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Error}]  Unknown config key: {key}[/]");
            return Task.CompletedTask;
        }

        key = property.Name;
        var appConfig = _getConfig();

        if (value == null)
        {
            var currentValue = property.GetValue(appConfig);
            var displayValue = currentValue?.ToString() ?? "(null)";
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Highlight}]  {key}:[/] {displayValue}");

            if (AppConfig.PropertyDescriptions.TryGetValue(key, out var desc))
                _host.AddMessage($"[{ThemeProvider.Current.Tools.General.Muted}]    → {Markup.Escape(desc)}[/]");
        }
        else
        {
            try
            {
                object? convertedValue;
                if (property.PropertyType == typeof(string))
                {
                    convertedValue = value;
                }
                else if (property.PropertyType == typeof(int))
                {
                    convertedValue = int.Parse(value);
                }
                else if (property.PropertyType == typeof(bool))
                {
                    convertedValue = bool.Parse(value);
                }
                else if (property.PropertyType == typeof(double))
                {
                    convertedValue = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (property.PropertyType.IsEnum)
                {
                    convertedValue = Enum.Parse(property.PropertyType, value, true);
                }
                else
                {
                    _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Error}]  Cannot set {key}: unsupported type {property.PropertyType.Name}[/]");
                    return Task.CompletedTask;
                }

                // Apply the change to a clone for validation before save
                var originalValue = property.GetValue(appConfig);

                // Set on the live config (AppConfigCommand owns this reference)
                property.SetValue(appConfig, convertedValue);

                // Validate before persisting
                var issues = _configService.Validate(appConfig);
                if (issues.Count > 0)
                {
                    _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  Validation warnings:[/]");
                    foreach (var issue in issues)
                        _host.AddMessage($"    [{ThemeProvider.Current.Tools.General.Muted}]• {Markup.Escape(issue)}[/]");
                    _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Info}]  The value will be saved despite warnings.[/]");
                }

                _configService.Save(appConfig);
                _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Success}]  {key} set to: {convertedValue}[/]");
            }
            catch (FormatException)
            {
                _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Error}]  Invalid value format for {key} (expected {property.PropertyType.Name})[/]");
            }
            catch (Exception ex)
            {
                _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Error}]  Failed to set {key}: {Markup.Escape(ex.Message)}[/]");
            }
        }

        return Task.CompletedTask;
    }
}

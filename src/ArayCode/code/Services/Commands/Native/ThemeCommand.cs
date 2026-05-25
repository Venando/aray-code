using System.Linq;
using ArayCode.Services.Themes;
using Spectre.Console;
using StreamShell;

namespace ArayCode.Services.Commands;

/// <summary>
/// Native command: /theme — shows current theme and available themes,
/// or switches to a named theme from the themes folder.
/// </summary>
public sealed class ThemeCommand : ICommand
{
    private readonly IStreamShellHost _host;
    private readonly ThemeService _themeService;
    private readonly IConfigurationService _configService;
    private readonly SessionHistoryService _historyService;
    private readonly Func<AppConfig> _getAppConfig;

    public string Name => "theme";
    public string Description => "Show current theme or switch to a theme. \"/theme\" to list, \"/theme <name>\" to switch";
    public CommandSource Source => CommandSource.Native;
    public ShellCommandType Type => ShellCommandType.Configuration;
    public string[]? Suggestions => _themeService.GetAvailableThemes();

    public ThemeCommand(
        IStreamShellHost host,
        ThemeService themeService,
        IConfigurationService configService,
        SessionHistoryService historyService,
        Func<AppConfig> getAppConfig)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        _getAppConfig = getAppConfig ?? throw new ArgumentNullException(nameof(getAppConfig));
    }

    public Task ExecuteAsync(string[] args, Dictionary<string, string> namedArgs, CancellationToken ct = default)
    {
        if (args.Length > 0)
            return HandleSwitchThemeAsync(args[0]);

        return HandleListThemeAsync();
    }

    private Task HandleListThemeAsync()
    {
        var currentName = _themeService.GetCurrentThemeName();
        var current = _themeService.CurrentTheme;
        var md = current.Markdown;
        var tools = current.Tools;
        var table = current.Table;

        _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Highlight}]Current theme:[/] [{ThemeProvider.Current.Tools.Messages.Emphasis}]{Markup.Escape(currentName)}[/]");
        _host.AddMessage($"  [{ThemeProvider.Current.Tools.General.Muted}]Author:[/] {Markup.Escape(current.Author)}");
        _host.AddMessage($"  [{ThemeProvider.Current.Tools.General.Muted}]Tools header:[/] [{tools.HeaderStyle}]{Markup.Escape(tools.HeaderStyle)}[/]");
        _host.AddMessage($"  [{ThemeProvider.Current.Tools.General.Muted}]Code fence start:[/] [dim]{Markup.Escape(md.CodeFenceStartMarkup)}[/]");
        _host.AddMessage($"  [{ThemeProvider.Current.Tools.General.Muted}]Code fence end:[/]   [dim]{Markup.Escape(md.CodeFenceEndMarkup)}[/]");
        _host.AddMessage($"  [{ThemeProvider.Current.Tools.General.Muted}]Code content:[/] [{md.CodeContentStyle}]{Markup.Escape(md.CodeContentStyle)}[/]");
        _host.AddMessage($"  [{ThemeProvider.Current.Tools.General.Muted}]Table edges:[/] [{table.EdgeColor}]{Markup.Escape(table.EdgeColor)}[/]");
        _host.AddMessage("");

        var available = _themeService.GetAvailableThemes();
        if (available.Length == 0)
        {
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  No custom theme files found in themes folder.[/]");
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Info}]  Use /appconfig to set ThemeFile to a theme name, or create a .json in the themes folder.[/]");
        }
        else
        {
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Highlight}]Available themes:[/]");
            foreach (var themeName in available)
            {
                var marker = string.Equals(themeName, currentName, StringComparison.OrdinalIgnoreCase)
                    ? $" [{ThemeProvider.Current.Tools.Messages.Emphasis}]►[/]"
                    : "  ";
                _host.AddMessage($"  {marker} {Markup.Escape(themeName)}");
            }
            _host.AddMessage("");
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Info}]  Use /theme <name> to switch[/]");
        }

        return Task.CompletedTask;
    }

    private async Task HandleSwitchThemeAsync(string themeName)
    {
        if (_themeService.TrySwapTheme(themeName))
        {
            var current = _themeService.CurrentTheme;
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Success}]Switched to theme:[/] [{ThemeProvider.Current.Tools.Messages.Emphasis}]{Markup.Escape(current.Name)}[/]");

            // Persist theme selection to config (DRY: same pattern as /reconfigure → ConfigWizardOrchestrator → configService.Save)
            var appConfig = _getAppConfig();
            appConfig.ThemeFile = _themeService.GetThemeFileName(themeName) ?? themeName;
            _configService.Save(appConfig);

            // Reload history for the current active agent (DRY: delegates to SessionHistoryService, same as /chat and /history)
            var sessionKey = AgentRegistry.ActiveSessionKey;
            if (sessionKey != null)
                await _historyService.PrintSessionHistoryAsync(sessionKey);
        }
        else
        {
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Error}]Theme not found or failed to load:[/] {Markup.Escape(themeName)}");
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Info}]  Theme files are stored in the themes folder as .json files.[/]");
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Info}]  Use /theme to list available themes.[/]");
        }
    }
}

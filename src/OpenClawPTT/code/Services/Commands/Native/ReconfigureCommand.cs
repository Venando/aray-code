using Spectre.Console;

namespace OpenClawPTT.Services.Commands;

/// <summary>Native command: /reconfigure — runs the reconfiguration wizard.</summary>
public sealed class ReconfigureCommand : ICommand
{
    private readonly IStreamShellHost _host;
    private readonly IConfigWizardOrchestrator _wizard;
    private readonly IConfigurationService _configService;

    public string Name => "reconfigure";
    public string Description => "Run reconfiguration wizard";
    public CommandSource Source => CommandSource.Native;
    public ShellCommandType Type => ShellCommandType.Configuration;
    public string[]? Suggestions => null;

    public ReconfigureCommand(IStreamShellHost host, IConfigWizardOrchestrator wizard, IConfigurationService configService)
    {
        _host = host;
        _wizard = wizard;
        _configService = configService;
    }

    public async Task ExecuteAsync(string[] args, Dictionary<string, string> namedArgs, CancellationToken ct = default)
    {
        var currentCfg = _configService.Load();
        if (currentCfg == null)
        {
            _host.AddMessage("[yellow]  No configuration found. Run first-time setup instead.[/]");
            return;
        }

        _host.AddMessage("[cyan2]  Starting reconfiguration wizard...[/]");
        try
        {
            var newCfg = await _wizard.ReconfigureAsync(_host, currentCfg, ct);
            _host.AddMessage("[green]  Configuration updated.[/]");
        }
        catch (OperationCanceledException)
        {
            _host.AddMessage("[grey]  Reconfiguration cancelled.[/]");
        }
    }
}

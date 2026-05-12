using Spectre.Console;

namespace OpenClawPTT.Services.Commands;

/// <summary>Native command: /llm — sends a message directly to the configured LLM.</summary>
public sealed class LlmCommand : ICommand
{
    private readonly IStreamShellHost _host;
    private readonly IColorConsole _console;
    private readonly IDirectLlmService? _directLlmService;
    private readonly AppConfig _appConfig;
    private readonly IStatusService? _statusService;

    public string Name => "llm";
    public string Description => "<message> Send message directly to configured LLM";
    public CommandSource Source => CommandSource.Native;
    public ShellCommandType Type => ShellCommandType.DirectLlm;
    public string[]? Suggestions => null;

    public LlmCommand(IStreamShellHost host, IColorConsole console, IDirectLlmService? directLlmService, AppConfig appConfig, IStatusService? statusService = null)
    {
        _host = host;
        _console = console;
        _directLlmService = directLlmService;
        _appConfig = appConfig;
        _statusService = statusService;
    }

    public async Task ExecuteAsync(string[] args, Dictionary<string, string> namedArgs, CancellationToken ct = default)
    {
        if (_directLlmService == null || !_directLlmService.IsConfigured)
        {
            _host.AddMessage("[yellow]  Direct LLM is not configured. Set DirectLlmUrl and DirectLlmModelName in config.[/]");
            return;
        }

        var message = string.Join(" ", args);
        if (string.IsNullOrWhiteSpace(message))
        {
            _host.AddMessage("[yellow]  Usage: /llm <your message>[/]");
            return;
        }

        _statusService?.SetDirectLlmLastCalled(DateTime.Now);

        _host.AddMessage($"[grey]  Sending to LLM ({_appConfig.DirectLlmModelName})...[/]");

        try
        {
            var response = await _directLlmService.SendAsync(message, ct);
            _statusService?.SetDirectLlmLastCalled(DateTime.Now);
            _console.PrintFormatted("[cyan]  LLM Response:[/] ", response);
        }
        catch (Exception ex)
        {
            _host.AddMessage($"[red]  LLM request failed: {Markup.Escape(ex.Message)}[/]");
        }
    }
}

using System.Collections.Generic;
using OpenClawPTT.Services;
using OpenClawPTT.Services.Themes;
using Spectre.Console;

namespace OpenClawPTT.Services.Commands;

/// <summary>
/// Forwards OpenClaw slash commands to the gateway.
/// /new and /reset are sent via <c>sessions.reset</c> RPC;
/// all other commands are forwarded as text messages via <c>chat.send</c>.
/// </summary>
public sealed class OpenClawForwardCommand : ICommand
{
    private readonly string _name;
    private readonly IStreamShellHost _host;
    private readonly ITextMessageSender _textSender;
    private readonly IGatewayService _gatewayService;
    private readonly IColorConsole _console;

    public string Name => _name;
    public string Description { get; }
    public CommandSource Source => CommandSource.OpenClaw;
    public ShellCommandType Type => OpenClawCommandMetadata.GetShellCommandType(_name);
    public string[]? Suggestions { get; }

    public OpenClawForwardCommand(
        string name,
        string description,
        IStreamShellHost host,
        ITextMessageSender textSender,
        IGatewayService gatewayService,
        IColorConsole console,
        string[]? suggestions = null)
    {
        _name = name;
        Description = description;
        _host = host;
        _textSender = textSender;
        _gatewayService = gatewayService;
        _console = console;
        Suggestions = suggestions;
    }

    public async Task ExecuteAsync(string[] args, Dictionary<string, string> namedArgs, CancellationToken ct = default)
    {
        // Intercept /new and /reset — use sessions.reset RPC directly
        if (string.Equals(_name, "reset", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_name, "new", StringComparison.OrdinalIgnoreCase))
        {
            var sessionKey = AgentRegistry.ActiveSessionKey;
            if (sessionKey == null)
            {
                _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Warning}]  No active session to reset.[/]");
                return;
            }

            var reason = string.Equals(_name, "new", StringComparison.OrdinalIgnoreCase) ? "new" : "reset";
            var displayCommand = "/" + _name;

            try
            {
                await _gatewayService.SendRpcAsync("sessions.reset", new Dictionary<string, object?>
                {
                    ["key"] = sessionKey,
                    ["reason"] = reason
                }, ct);
                _console.PrintMarkupedUserMessage($"[{ThemeProvider.Current.Tools.Messages.ForwardedCommandStyle}]⚡ {Markup.Escape(displayCommand)} [/]");
                _console.PrintMarkup("");
            }
            catch (Exception ex)
            {
                _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Error}]  Failed to reset session: {Markup.Escape(ex.Message)}[/]");
            }
            return;
        }

        // For all other commands, forward as text via chat.send
        var parts = new List<string> { "/" + _name };
        parts.AddRange(args);
        foreach (var kvp in namedArgs)
            parts.Add($"{kvp.Key}={kvp.Value}");

        var commandText = string.Join(" ", parts);

        try
        {
            await _textSender.SendAsync(commandText, ct, printMessage: false);
            _console.PrintMarkupedUserMessage($"[{ThemeProvider.Current.Tools.Messages.ForwardedCommandStyle}]⚡ {Markup.Escape(commandText)} [/]");
            _console.PrintMarkup("");
        }
        catch (Exception ex)
        {
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Error}]  Failed to send command: {Markup.Escape(ex.Message)}[/]");
        }
    }
}

using Spectre.Console;
using StreamShell;
using OpenClawPTT.Services.Themes;

namespace OpenClawPTT.Services.Commands;

/// <summary>
/// Central registry for all PTT commands — both native and OpenClaw.
/// Registers commands with StreamShell, wraps their handlers to fire
/// <see cref="CommandExecuted"/> for every execution, and provides
/// uniform access to command metadata.
/// </summary>
public sealed class CommandRegistry
{
    private readonly IStreamShellHost _host;
    private readonly List<ICommand> _commands = new();

    /// <summary>
    /// Raised whenever ANY command (native or OpenClaw) is executed.
    /// This replaces the per-handler event scattering that previously
    /// only fired for OpenClaw commands in <see cref="AgentSwitchingCommands"/>.
    /// </summary>
    public event EventHandler<CommandExecutedEventArgs>? CommandExecuted;

    public CommandRegistry(IStreamShellHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>Registers a command. Native commands should be registered individually.</summary>
    public void Register(ICommand command)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));
        _commands.Add(command);

        var style = ThemeProvider.Current.Tools.StreamShell.NativeCommandNameStyle;
        var nameMarkup = command.Source == CommandSource.Native
            ? $"[{style}]{command.Name}[/]"
            : command.Name;

        _host.AddCommand(new Command(
            nameMarkup,
            Markup.Escape(command.Description),
            (args, named) => WrapHandler(command, args, named),
            command.Suggestions));
    }

    /// <summary>Registers all known OpenClaw commands using the shared forwarder logic.</summary>
    public void RegisterOpenClawCommands(
        ITextMessageSender textSender,
        IGatewayService gatewayService,
        IColorConsole console)
    {
        foreach (var name in OpenClawCommandMetadata.Names)
        {
            var description = OpenClawCommandMetadata.GetDescription(name) ?? "OpenClaw command";
            var suggestions = OpenClawCommandSuggestions.Get(name);

            var cmd = new OpenClawForwardCommand(
                name, description, _host, textSender, gatewayService, console, suggestions);

            Register(cmd);
        }
    }

    /// <summary>Removes a previously registered command by name. No-op if not found.</summary>
    public void Unregister(string name)
    {
        _commands.RemoveAll(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        try { _host.RemoveCommand(name); }
        catch (InvalidOperationException)
        {
            // StreamShell may already have cleaned up (e.g., during disposal)
        }
    }

    /// <summary>All registered commands.</summary>
    public IReadOnlyList<ICommand> Commands => _commands;

    private Task WrapHandler(ICommand command, string[] args, Dictionary<string, string> named)
    {
        // Fire the unified event BEFORE execution so subscribers can react
        // (e.g. conversation naming clearing on /reset)
        var eventArgs = new CommandExecutedEventArgs(
            command.Name, command.Source, command.Type, args, named);
        CommandExecuted?.Invoke(this, eventArgs);

        return command.ExecuteAsync(args, named, CancellationToken.None);
    }
}

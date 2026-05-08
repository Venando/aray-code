using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.Services;
using OpenClawPTT.Services.Diagnostics;
using Spectre.Console;
using StreamShell;

namespace OpenClawPTT;

/// <summary>
/// Integrates StreamShell with the PTT client — registers StreamShell commands
/// (/quit, /reconfigure, /crew, /chat, OpenClash slash commands) and wires up
/// non-command user text input to the gateway text sender.
/// Agent settings commands (hotkey, emoji) and text composition are delegated
/// to separate classes for Single Responsibility.
/// </summary>
public sealed class StreamShellInputHandler : IDisposable
{
    private readonly IStreamShellHost _host;
    private readonly ITextMessageSender _textSender;
    private readonly IGatewayService _gatewayService;
    private readonly IConfigurationService _configService;
    private readonly IDirectLlmService? _directLlmService;
    private readonly AppConfig _appConfig;
    private readonly Action _onQuit;
    private readonly AgentSwitchingCommands _agentSwitching;
    private readonly TextMessageComposer _messageComposer;
    private readonly IColorConsole _console;
    private readonly IAgentSettingsPersistence _agentSettingsPersistence;
    private readonly IPttStateMachine _pttStateMachine;
    private readonly ITtsSummarizer? _ttsSummarizer;
    private readonly ErrorLogStore _errorLog;

    public StreamShellInputHandler(
        IStreamShellHost host,
        ITextMessageSender textSender,
        IGatewayService gatewayService,
        IConfigurationService configService,
        AppConfig appConfig,
        Action onQuit,
        IColorConsole console,
        IAgentSettingsPersistence agentSettingsPersistence,
        IPttStateMachine pttStateMachine,
        IDirectLlmService? directLlmService = null,
        ITtsSummarizer? ttsSummarizer = null,
        ErrorLogStore? errorLogStore = null)
    {
        _host = host;
        _textSender = textSender;
        _gatewayService = gatewayService;
        _configService = configService;
        _onQuit = onQuit;
        _appConfig = appConfig;
        _directLlmService = directLlmService;
        _console = console;
        _agentSettingsPersistence = agentSettingsPersistence;
        _pttStateMachine = pttStateMachine;
        _ttsSummarizer = ttsSummarizer;
        _errorLog = errorLogStore ?? new ErrorLogStore(appConfig.DataDir);
        _agentSwitching = new AgentSwitchingCommands(host, textSender, gatewayService, appConfig, console, agentSettingsPersistence, pttStateMachine, _configService, _errorLog);
        _messageComposer = new TextMessageComposer(host, textSender);
    }

    /// <summary>Register all commands and the UserInputSubmitted handler.</summary>
    public async Task RegisterAsync()
    {
        // Application commands
        _host.AddCommand(new Command("quit", "Exit the application", QuitHandler));
        _host.AddCommand(new Command("reconfigure", "Run reconfiguration wizard", ReconfigureHandler));
        _host.AddCommand(new Command("crew", "List available agents. \"/crew config\" to config", CrewHandler));
        _host.AddCommand(new Command("chat", "<name|id> Switch active agent by name or ID", ChatHandler));

        // Direct LLM command (bypasses agent)
        _host.AddCommand(new Command("llm", "<message> Send message directly to configured LLM", LlmHandler));

        // TTS summary test command
        _host.AddCommand(new Command("tts-test", "Test TTS summarization pipeline with sample file", LlmTestSummaryHandler));

        // Screen management
        _host.AddCommand(new Command("clean", "Clear the terminal screen",
            (args, named) => { _host.Clear(); return Task.CompletedTask; }));

        // Diagnostics commands
        _host.AddCommand(new Command("errors", "[N] Show recent gateway errors",
            (args, named) => _agentSwitching.HandleErrorsCommand(args)));
        _host.AddCommand(new Command("reconnect", "Reconnect to the gateway",
            (args, named) => _agentSwitching.HandleReconnectCommand(args)));

        // AppConfig command to get/set any config value (named 'appconfig' to avoid
        // conflict with OpenClaw's built-in /config command).
        _host.AddCommand(new Command("appconfig", Markup.Escape("<key> [value] Get or set app config value"), ConfigHandler));

        // OpenClaw tool commands (for StreamShell hint support)
        foreach (var name in OpenClawCommands.Names)
        {
            var cmdName = name; // Capture for closure
            _host.AddCommand(new Command(name, Markup.Escape(OpenClawCommands.Descriptions[name]),
                (args, named) => OpenClawCommandForwarder(cmdName, args, named)));
        }

        _host.UserInputSubmitted += OnUserInput;

        // Fetch initial session history after commands are registered
        var sessionKey = AgentRegistry.ActiveSessionKey;
        if (sessionKey != null)
            await _agentSwitching.PrintSessionHistory(sessionKey);
    }

    public void Dispose()
    {
        _host.UserInputSubmitted -= OnUserInput;
        // ErrorLogStore is owned by AppRunner — do not dispose here
    }

    private Task QuitHandler(string[] args, System.Collections.Generic.Dictionary<string, string> named)
    {
        _host.AddMessage("[green]  Bye![/]");
        _onQuit();
        return Task.CompletedTask;
    }

    private async Task ReconfigureHandler(string[] args, System.Collections.Generic.Dictionary<string, string> named)
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
            var newCfg = await _configService.ReconfigureAsync(_host, currentCfg, CancellationToken.None);
            _host.AddMessage("[green]  Configuration updated.[/]");
        }
        catch (OperationCanceledException)
        {
            _host.AddMessage("[grey]  Reconfiguration cancelled.[/]");
        }
    }

    /// <summary>
    /// Handles user input from StreamShell.
    /// For plain text, sends it as a message (with attachment content prepended).
    /// For commands, StreamShell auto-executes via command registration — nothing to do here.
    /// </summary>
    private void OnUserInput(string input, InputType type, IReadOnlyList<Attachment> attachments)
    {
        // Commands are auto-executed by StreamShell — skip
        if (type == InputType.Command)
            return;

        // Mark as typed input (not voice) — clear agent so SISO won't match a different agent
        _pttStateMachine.LastInputWasVoice = false;
        _pttStateMachine.LastTargetAgent = null;

        _messageComposer.TryToComposeMessage(input, attachments, out string? composedMessage);

        // Use non-blocking send via fire-and-forget since StreamShell fires events synchronously.
        // Exceptions are caught and surfaced inside SendWithAttachmentsAsync.
        _ = Task.Run(async () =>
        {
            try
            {
                await _messageComposer.SendWithAttachmentsAsync(composedMessage!, CancellationToken.None);
            }
            catch (Exception ex)
            {
                var classification = GatewayErrorClassifier.Classify(ex);
                _errorLog.Write(classification.ToLogEntry());
                _host.AddMessage($"[red]  Failed to send: {classification.HumanMessage}[/]");
            }
        });
    }

    private Task CrewHandler(string[] args, System.Collections.Generic.Dictionary<string, string> named)
    {
        if (args.Length > 0 && args[0].Equals("config", System.StringComparison.OrdinalIgnoreCase))
            return _agentSwitching.HandleConfigCommand(args.Skip(1).ToArray());

        return _agentSwitching.HandleCrew(args);
    }

    private Task ChatHandler(string[] args, System.Collections.Generic.Dictionary<string, string> named)
    {
        return _agentSwitching.HandleChat(args);
    }

    private async Task LlmHandler(string[] args, System.Collections.Generic.Dictionary<string, string> named)
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

        _host.AddMessage($"[grey]  Sending to LLM ({_appConfig.DirectLlmModelName})...[/]");

        try
        {
            var response = await _directLlmService.SendAsync(message, CancellationToken.None);
            _console.PrintFormatted("[cyan]  LLM Response:[/] ", response);
        }
        catch (Exception ex)
        {
            _host.AddMessage($"[red]  LLM request failed: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private async Task LlmTestSummaryHandler(string[] args, System.Collections.Generic.Dictionary<string, string> named)
    {
        if (_ttsSummarizer == null)
        {
            _host.AddMessage("[yellow]  TTS summarizer not available. Make sure DirectLlmUrl is configured.[/]");
            return;
        }

        // Load sample file from the app's output directory
        var samplePath = Path.Combine(AppContext.BaseDirectory, "test-summary-sample.txt");
        if (!File.Exists(samplePath))
        {
            _host.AddMessage($"[red]  Sample file not found: {samplePath}[/]");
            return;
        }


        var rawText = await File.ReadAllTextAsync(samplePath);
        _host.AddMessage($"[grey]  Loaded sample ({rawText.Length} chars raw)[/]");


        _host.AddMessage($"[grey]  Running through TTS preprocessing...[/]");
        var preprocessed = TtsContentFilter.SanitizeForTts(rawText);
        _host.AddMessage($"[grey]  After sanitize: {preprocessed.Length} chars[/]:[white]{preprocessed}[/]");

        _host.AddMessage($"[grey]  Sending to LLM ({_appConfig.DirectLlmModelName}) for summarization...[/]");
        try
        {
            var summarized = await _ttsSummarizer.SummarizeForTtsAsync(rawText, _appConfig, CancellationToken.None);
            _host.AddMessage($"[green]  Summary ({summarized.Length} chars):[/]");
            _host.AddMessage($"  {Markup.Escape(summarized)}");
        }
        catch (Exception ex)
        {
            _host.AddMessage($"[red]  Summarization failed: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private Task OpenClawCommandForwarder(string commandName, string[] args, System.Collections.Generic.Dictionary<string, string> named)
    {
        return _agentSwitching.HandleOpenClawCommand(commandName, args, named);
    }

    private Task ConfigHandler(string[] args, System.Collections.Generic.Dictionary<string, string> named)
    {
        if (args.Length == 0)
        {
            _host.AddMessage("[yellow]  Usage: /appconfig <key> [value][/]");
            _host.AddMessage("[grey]  Examples:[/]");
            _host.AddMessage("    /appconfig DirectLlmUrl           (show current value)");
            _host.AddMessage("    /appconfig DirectLlmUrl http://... (set new value)");
            return Task.CompletedTask;
        }

        var key = args[0];
        var value = args.Length > 1 ? string.Join(" ", args.Skip(1)) : null;

        // Use reflection to get/set property (case-insensitive)
        var property = typeof(AppConfig).GetProperty(key, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
        if (property == null)
        {
            _host.AddMessage($"[red]  Unknown config key: {key}[/]");
            return Task.CompletedTask;
        }

        // Normalize to the canonical property name for consistent description lookup
        key = property.Name;

        if (value == null)
        {
            // Get current value
            var currentValue = property.GetValue(_appConfig);
            var displayValue = currentValue?.ToString() ?? "(null)";
            _host.AddMessage($"[cyan]  {key}:[/] {displayValue}");

            if (AppConfig.PropertyDescriptions.TryGetValue(key, out var desc))
                _host.AddMessage($"[grey]    → {Markup.Escape(desc)}[/]");
        }
        else
        {
            // Set new value
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
                    _host.AddMessage($"[red]  Cannot set {key}: unsupported type {property.PropertyType.Name}[/]");
                    return Task.CompletedTask;
                }

                property.SetValue(_appConfig, convertedValue);
                _configService.Save(_appConfig);
                _host.AddMessage($"[green]  {key} set to: {convertedValue}[/]");
            }
            catch (FormatException)
            {
                _host.AddMessage($"[red]  Invalid value format for {key} (expected {property.PropertyType.Name})[/]");
            }
            catch (Exception ex)
            {
                _host.AddMessage($"[red]  Failed to set {key}: {Markup.Escape(ex.Message)}[/]");
            }
        }

        return Task.CompletedTask;
    }
}

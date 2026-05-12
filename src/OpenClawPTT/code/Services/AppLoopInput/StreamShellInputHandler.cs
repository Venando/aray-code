using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.ConfigWizard;
using OpenClawPTT.Services;
using OpenClawPTT.Services.Commands;
using OpenClawPTT.Services.Diagnostics;
using Spectre.Console;
using StreamShell;

namespace OpenClawPTT;

/// <summary>
/// Integrates StreamShell with the PTT client.
/// Registers all native and OpenClaw commands via <see cref="CommandRegistry"/>
/// and wires up non-command user text input to the gateway text sender.
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
    private readonly TextMessageComposer _messageComposer;
    private readonly IColorConsole _console;
    private readonly IAgentSettingsPersistence _agentSettingsPersistence;
    private readonly IPttStateMachine _pttStateMachine;
    private readonly ITtsSummarizer? _ttsSummarizer;
    private readonly ErrorLogStore _errorLog;
    private readonly IStatusService _statusService;
    private readonly SessionHistoryService _historyService;
    private readonly CommandRegistry _registry;

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
        ErrorLogStore? errorLogStore = null,
        IStatusService? statusService = null)
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
        _statusService = statusService ?? new StatusService(host);
        _messageComposer = new TextMessageComposer(host, textSender);
        _historyService = new SessionHistoryService(host, gatewayService, console, pttStateMachine, appConfig);
        _registry = new CommandRegistry(host);
    }

    /// <summary>Register all commands and the UserInputSubmitted handler.</summary>
    public async Task RegisterAsync()
    {
        // ── Native commands ───────────────────────────────────────────────
        _registry.Register(new QuitCommand(_host, _onQuit));
        _registry.Register(new ReconfigureCommand(_host, _configService));
        _registry.Register(new CrewCommand(_host, _agentSettingsPersistence, _appConfig, _historyService, _configService));
        _registry.Register(new ChatCommand(_host, _configService, _historyService, _appConfig));
        _registry.Register(new LlmCommand(_host, _console, _directLlmService, _appConfig));
        _registry.Register(new TtsTestCommand(_host, _ttsSummarizer, _appConfig));
        _registry.Register(new CleanCommand(_host));
        _registry.Register(new HistoryCommand(_host, _gatewayService, _console, _pttStateMachine, _appConfig));
        _registry.Register(new ErrorsCommand(_host, _errorLog));
        _registry.Register(new ReconnectCommand(_host, _gatewayService, _console, _statusService, _errorLog, _historyService));
        _registry.Register(new AppConfigCommand(_host, _appConfig, _configService));

        // ── OpenClaw forwarded commands ───────────────────────────────────
        // Registered regardless of connection state; execution-time guards handle
        // the disconnected case (e.g. /reset without an active session shows a
        // user-friendly message instead of crashing).
        _registry.RegisterOpenClawCommands(_textSender, _gatewayService, _console);

        // ── Direct LLM command — only when the service is configured ─────────
        if (_directLlmService != null && _directLlmService.IsConfigured)
            _registry.Register(new LlmCommand(_host, _console, _directLlmService, _appConfig, _statusService));

        _host.UserInputSubmitted += OnUserInput;

        // Fetch initial session history after commands are registered
        var sessionKey = AgentRegistry.ActiveSessionKey;
        if (sessionKey != null)
            await _historyService.PrintSessionHistoryAsync(sessionKey);
    }

    /// <summary>
    /// Raised whenever ANY command (native or OpenClaw) is executed.
    /// Carries rich metadata: name, source, type, and arguments.
    /// </summary>
    public event EventHandler<CommandExecutedEventArgs>? CommandExecuted
    {
        add => _registry.CommandExecuted += value;
        remove => _registry.CommandExecuted -= value;
    }

    /// <summary>Exposes session history printing for wiring with AgentHotkeyService.</summary>
    public Task PrintSessionHistory(string sessionKey) =>
        _historyService.PrintSessionHistoryAsync(sessionKey);

    public void Dispose()
    {
        _host.UserInputSubmitted -= OnUserInput;
    }

    /// <summary>
    /// Handles user input from StreamShell.
    /// For plain text, sends it as a message.
    /// For commands, StreamShell auto-executes via command registration.
    /// </summary>
    private void OnUserInput(StreamShell.UserInputSubmittedEventArgs e)
    {
        if (e.InputType == StreamShell.InputType.Command)
            return;

        // Skip if a configuration wizard is active (it handles its own input)
        if (ModularConfigurationWizard.IsActive)
            return;

        // Reject plain-text messages that start with "/" — they look like commands
        // but weren't recognized by StreamShell. Don't send them to the gateway.
        if (e.RawOutput.StartsWith("/", StringComparison.Ordinal))
        {
            var commandName = e.RawOutput.Split(' ')[0];
            _host.AddMessage($"[red]  Unknown command: {commandName}[/]");
            return;
        }

        _pttStateMachine.LastInputWasVoice = false;
        _pttStateMachine.LastTargetAgent = null;

        string composedMessage = e.TextWithAttachmentsExpanded;

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
}

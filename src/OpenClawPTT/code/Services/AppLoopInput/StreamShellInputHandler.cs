using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenClawPTT.ConfigWizard;
using OpenClawPTT.Services;
using OpenClawPTT.Services.Commands;
using OpenClawPTT.Services.Diagnostics;
using OpenClawPTT.Services.Themes;
using Spectre.Console;
using StreamShell;

namespace OpenClawPTT;

/// <summary>
/// Integrates StreamShell with the PTT client.
/// Registers all native and OpenClaw commands via <see cref="CommandRegistry"/>
/// and wires up non-command user text input to the gateway text sender.
///
/// Commands are split into three visibility groups:
///   1. Always-on (no dependencies) — registered once in <see cref="RegisterBaseAsync"/>.
///   2. Gateway-dependent — shown/hidden via <see cref="SetGatewayConnected"/>.
///   3. Direct-LLM-dependent — shown/hidden via <see cref="SetDirectLlmConfigured"/>.
/// </summary>
public sealed class StreamShellInputHandler : IDisposable
{
    private readonly IStreamShellHost _host;
    private readonly ITextMessageSender _textSender;
    private readonly IGatewayService _gatewayService;
    private readonly IConfigurationService _configService;
    private readonly IConfigWizardOrchestrator? _wizard;
    private readonly IDirectLlmService? _directLlmService;
    private AppConfig _appConfig;
    private readonly Action _onQuit;
    private readonly TextMessageComposer _messageComposer;
    private readonly IColorConsole _console;
    private readonly IAgentSettingsPersistence _agentSettingsPersistence;
    private readonly IPttStateMachine _pttStateMachine;
    private readonly ITtsSummarizer? _ttsSummarizer;
    private readonly IConversationNamingService? _namingService;
    private readonly ErrorLogStore _errorLog;
    private readonly IStatusService _statusService;
    private readonly SessionHistoryService _historyService;

    /// <summary>Exposed so AppRunner can wire it into the bottom panel.</summary>
    public SessionHistoryService HistoryService => _historyService;

    private readonly CommandRegistry _registry;
    private readonly ThemeService _themeService;

    // ── Group-tracking state ──────────────────────────────────────────────
    private bool _gatewayCommandsRegistered;
    private bool _llmCommandRegistered;
    private string? _lastKnownLlmUrl;
    private string? _lastKnownLlmModel;
    private bool _disposed;

    // Singleton list of all OpenClaw command names, ordered for determinism
    private static readonly string[] OpenClawCommandNames =
        OpenClawCommandMetadata.Names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();

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
        IConversationNamingService? namingService = null,
        ErrorLogStore? errorLogStore = null,
        IStatusService? statusService = null,
        IConfigWizardOrchestrator? wizard = null,
        ThemeService? themeService = null)
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
        _namingService = namingService;
        _wizard = wizard;
        _errorLog = errorLogStore ?? new ErrorLogStore(appConfig.DataDir);
        _statusService = statusService ?? new StatusService(host);
        _messageComposer = new TextMessageComposer(host, textSender);
        _historyService = new SessionHistoryService(host, gatewayService, console, pttStateMachine, appConfig);
        _registry = new CommandRegistry(host);
        _themeService = themeService ?? new ThemeService(appConfig);

        // Initialize theme: ensure example file exists, then load the configured theme
        _themeService.EnsureExampleFile();
        _themeService.LoadTheme();
        _host.ApplyStreamShellTheme(ComputePrefixWidth());
        _statusService.RefreshTheme();

        // Re-apply StreamShell settings on runtime theme swap
        _themeService.ThemeChanged += (_, _) =>
        {
            _host.ApplyStreamShellTheme(ComputePrefixWidth());
            _statusService.RefreshTheme();
        };

        _lastKnownLlmUrl = appConfig.DirectLlmUrl;
        _lastKnownLlmModel = appConfig.DirectLlmModelName;
    }

    // ── Public registration API ───────────────────────────────────────────

    /// <summary>
    /// Registers always-on commands (no gateway or Direct LLM dependency),
    /// wires UserInputSubmitted, and subscribes to ConfigSaved for dynamic
    /// Direct LLM command visibility.
    /// </summary>
    public async Task RegisterBaseAsync()
    {
        // ── Always-on native commands ──────────────────────────────────────
        _registry.Register(new QuitCommand(_host, _onQuit));
        _registry.Register(new ReconfigureCommand(_host, _wizard ?? new ConfigWizardOrchestrator(_configService), _configService));
        _registry.Register(new CrewCommand(_host, _agentSettingsPersistence, _appConfig, _historyService, _configService));
        _registry.Register(new ChatCommand(_host, _configService, _historyService, _appConfig));
        _registry.Register(new CleanCommand(_host));
        _registry.Register(new ErrorsCommand(_host, _errorLog));

        // /reconnect is always available — it's how users fix a broken connection
        var reconnectCmd = new ReconnectCommand(_host, _gatewayService, _console, _statusService, _errorLog, _historyService);
        reconnectCmd.OnReconnectSuccess = OnGatewayReconnected;
        _registry.Register(reconnectCmd);

        _registry.Register(new AppConfigCommand(_host, _appConfig, _configService));
        _registry.Register(new AppStatusCommand(_host, _statusService, _appConfig));
        _registry.Register(new ThemeCommand(_host, _themeService, _configService, _historyService, _appConfig));

        // ── Wire input handling ────────────────────────────────────────────
        _host.UserInputSubmitted += OnUserInput;

        // ── Subscribe to config changes for Direct LLM command visibility ──
        _configService.ConfigSaved += OnConfigSaved;

        // Gateway-dependent commands (/history, all OpenClaw commands) and
        // Direct-LLM commands (/llm) are NOT registered here.
        // They are added via SetGatewayConnected() / SetDirectLlmConfigured()
        // which AppRunner calls after determining connection state.
        await Task.CompletedTask;
    }

    // ── Dynamic command visibility ────────────────────────────────────────

    /// <summary>
    /// Shows or hides gateway-dependent commands based on connection state.
    /// When connected, registers <c>/history</c> and all ~60 OpenClaw commands.
    /// When disconnected, removes them from the palette.
    /// </summary>
    public void SetGatewayConnected(bool connected)
    {
        if (connected && !_gatewayCommandsRegistered)
        {
            RegisterGatewayCommands();
            _gatewayCommandsRegistered = true;

            // Fetch session history now that gateway-dependent commands are available
            var sessionKey = AgentRegistry.ActiveSessionKey;
            if (sessionKey != null)
                _ = TryFetchHistoryAsync(sessionKey);

            // Bootstrap AgentActivityStore for all other agents (limit=3, no display)
            foreach (var agent in AgentRegistry.Agents)
            {
                if (agent.SessionKey != sessionKey)
                    _ = BootstrapAgentHistoryAsync(agent.SessionKey);
            }
        }
        else if (!connected && _gatewayCommandsRegistered)
        {
            UnregisterGatewayCommands();
            _gatewayCommandsRegistered = false;
        }
    }

    /// <summary>
    /// Shows or hides the <c>/llm</c> command based on whether Direct LLM is configured.
    /// </summary>
    public void SetDirectLlmConfigured(bool configured)
    {
        if (configured && !_llmCommandRegistered)
        {
            RegisterDirectLlmCommand();
            _llmCommandRegistered = true;
        }
        else if (!configured && _llmCommandRegistered)
        {
            UnregisterDirectLlmCommand();
            _llmCommandRegistered = false;
        }
    }

    // ── Group: gateway-dependent commands ─────────────────────────────────

    private void RegisterGatewayCommands()
    {
        // Native gateway-dependent commands
        _registry.Register(new HistoryCommand(_host, _gatewayService, _console, _pttStateMachine, _appConfig));

        // All OpenClaw forwarded commands
        foreach (var name in OpenClawCommandNames)
        {
            var description = OpenClawCommandMetadata.GetDescription(name) ?? "OpenClaw command";
            var suggestions = OpenClawCommandSuggestions.Get(name);

            var cmd = new OpenClawForwardCommand(
                name, description, _host, _textSender, _gatewayService, _console, suggestions);

            _registry.Register(cmd);
        }
    }

    private void UnregisterGatewayCommands()
    {
        // Remove native gateway-dependent command
        _registry.Unregister("history");

        // Remove all OpenClaw forwarded commands
        foreach (var name in OpenClawCommandNames)
            _registry.Unregister(name);
    }

    // ── Group: Direct-LLM-dependent commands ──────────────────────────────

    private void RegisterDirectLlmCommand()
    {
        // Check the current config rather than the captured service reference.
        // The _directLlmService may be null when Direct LLM is configured at
        // runtime via /appconfig after startup (Scenario C).  In that case we
        // still register /llm — LlmCommand.ExecuteAsync gracefully displays
        // "not configured" if the service is unavailable, prompting the user
        // to restart.  A future improvement could re-create IDirectLlmService
        // from IServiceFactory here.
        bool hasConfig = !string.IsNullOrWhiteSpace(_appConfig.DirectLlmUrl) &&
                         !string.IsNullOrWhiteSpace(_appConfig.DirectLlmModelName);
        if (!hasConfig)
            return;
        _registry.Register(new LlmCommand(_host, _console, _directLlmService, _appConfig, _ttsSummarizer, _namingService));
    }

    private void UnregisterDirectLlmCommand()
    {
        _registry.Unregister("llm");
    }

    // ── Reconnect callback ────────────────────────────────────────────────

    /// <summary>
    /// Called by ReconnectCommand after a successful reconnection.
    /// Re-registers gateway-dependent commands so they appear in the palette.
    /// Session history is fetched by <see cref="ReconnectCommand.ExecuteAsync"/>
    /// after this callback completes, so we don't duplicate the fetch here.
    /// </summary>
    private async Task OnGatewayReconnected()
    {
        // Force re-register: unregister first, then register fresh
        if (_gatewayCommandsRegistered)
        {
            UnregisterGatewayCommands();
            _gatewayCommandsRegistered = false;
        }

        RegisterGatewayCommands();
        _gatewayCommandsRegistered = true;

        await Task.CompletedTask;
    }

    // ── Config change handler for Direct LLM ──────────────────────────────

    /// <summary>
    /// Detects DirectLlmUrl / DirectLlmModelName changes and adds or removes
    /// the <c>/llm</c> command dynamically.
    /// Updates <c>_appConfig</c> so that <c>RegisterDirectLlmCommand</c>
    /// reads the current values, not the stale startup ones.
    /// </summary>
    private void OnConfigSaved(ConfigChangedEventArgs e)
    {
        // Keep _appConfig in sync — RegisterDirectLlmCommand() reads it.
        _appConfig = e.NewConfig;

        bool llmChanged = e.AnyChanged(nameof(AppConfig.DirectLlmUrl), nameof(AppConfig.DirectLlmModelName));
        if (!llmChanged)
            return;

        var llmUrl = e.NewConfig.DirectLlmUrl;
        var llmModel = e.NewConfig.DirectLlmModelName;

        _lastKnownLlmUrl = llmUrl;
        _lastKnownLlmModel = llmModel;

        // Update the existing service's config so subsequent /llm calls
        // use the new URL/model/token without requiring a restart.
        _directLlmService?.UpdateConfig(e.NewConfig);

        bool nowConfigured = !string.IsNullOrWhiteSpace(llmUrl) && !string.IsNullOrWhiteSpace(llmModel);
        SetDirectLlmConfigured(nowConfigured);
    }

    // ── Session history fetch (fire-and-forget with error handling) ───────

    private async Task TryFetchHistoryAsync(string sessionKey)
    {
        try
        {
            await _historyService.PrintSessionHistoryAsync(sessionKey);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to fetch session history: {ex.Message}");
        }
    }

    /// <summary>
    /// Fetches the last 3 history entries for a non-active agent to populate
    /// <see cref="AgentActivityStore"/> on boot. Does not display anything.
    /// </summary>
    private async Task BootstrapAgentHistoryAsync(string sessionKey)
    {
        try
        {
            // limit=3: enough for status display, minimal bandwidth
            _ = await _gatewayService.FetchSessionHistoryAsync(sessionKey, limit: 3);
            // Result is intentionally discarded — we only need the side effect
            // of ExtractAgentStatusFromHistory populating the activity store.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to bootstrap agent history for {sessionKey}: {ex.Message}");
        }
    }

    // ── Public events & wiring ────────────────────────────────────────────

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
        if (_disposed) return;
        _disposed = true;

        _host.UserInputSubmitted -= OnUserInput;
        _configService.ConfigSaved -= OnConfigSaved;
    }

    // ── User input handling ───────────────────────────────────────────────

    /// <summary>
    /// Handles user input from StreamShell.
    /// For plain text, sends it as a message.
    /// For commands, StreamShell auto-executes via command registration.
    /// </summary>
    private void OnUserInput(StreamShell.UserInputSubmittedEventArgs e)
    {
        if (e.InputType == StreamShell.InputType.Command)
            return;

        // Skip if any wizard is active (it handles its own input)
        if (WizardState.IsActive)
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

    /// <summary>
    /// Computes the visual width of the user message prefix for input prompt alignment.
    /// Reads from the active theme so /theme changes realign the prompt.
    /// </summary>
    private int ComputePrefixWidth()
        => Markup.Remove(ThemeProvider.Current.Tools.StatusBar.UserMessagePrefix).Length;
}

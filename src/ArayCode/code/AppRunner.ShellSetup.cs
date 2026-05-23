namespace ArayCode;

using ArayCode.Services;
using ArayCode.Services.Commands;
using ArayCode.Services.Diagnostics;
using ArayCode.TTS;

/// <summary>
/// Partial class for StreamShell and hotkey service creation.
/// </summary>
public partial class AppRunner
{
    /// <summary>
    /// Creates and registers all StreamShell commands and the agent hotkey service.
    /// Also wires session snapshot cleanup and command-executed events.
    /// </summary>
    private async Task<(
            AgentHotkeyService HotkeyService,
            StreamShellInputHandler ShellCommands,
            SessionResetSnapshotCleaner SnapshotCleaner,
            IAppLoop PttLoop)>
        CreateShellAndHotkeyServicesAsync(
            IGatewayService gateway,
            IPttStateMachine pttStateMachine,
            ITextMessageSender textSender,
            IConversationNamingService? namingService,
            IDirectLlmService directLlmService,
            ITtsSummarizer ttsSummarizer,
            IAudioService audioService,
            Services.IInputHandler inputHandler,
            bool gatewayConnected,
            CancellationToken ct)
    {
        var pttController = new PttController();

        var agentHotkeyService = new AgentHotkeyService(
            pttController, textSender, _shellHost, _cfg,
            _factory.GetAgentSettingsPersistence(),
            gatewayService: gateway,
            pttStateMachine: pttStateMachine,
            console: _console);

        var shellCommands = new StreamShellInputHandler(
            _shellHost,
            textSender,
            gateway,
            _configService,
            _cfg,
            onQuit: () => _cts?.Cancel(),
            console: _console,
            agentSettingsPersistence: _factory.GetAgentSettingsPersistence(),
            pttStateMachine: pttStateMachine,
            directLlmService: directLlmService.IsConfigured ? directLlmService : null,
            ttsSummarizer: ttsSummarizer,
            namingService: namingService,
            errorLogStore: _errorLog,
            statusService: _statusService,
            wizard: _wizard
        );
        if (namingService != null)
            shellCommands.CommandExecuted += namingService.OnCommandExecuted;

        // Wire history service into the bottom panel for full agent-switch behaviour.
        // Also store in StreamShellHost so recreated panels (after wizards) get it too.
        if (_bottomPanel is AgentStatusBottomPanel asp)
        {
            asp.SetHistoryService(shellCommands.HistoryService);
            _shellHost.SetPanelHistoryService(shellCommands.HistoryService);
        }

        var snapshotCleaner = new SessionResetSnapshotCleaner(_factory.AgentActivityStore);
        shellCommands.CommandExecuted += snapshotCleaner.Handle;

        await shellCommands.RegisterBaseAsync();

        if (gatewayConnected)
            shellCommands.SetGatewayConnected(true);

        if (directLlmService.IsConfigured)
            shellCommands.SetDirectLlmConfigured(true);

        // Wire agent hotkey history printing to the canonical shared method
        agentHotkeyService.PrintSessionHistoryAsync = shellCommands.PrintSessionHistory;

        _console.PrintHelpMenu(_cfg);

        var pttLoop = _factory.CreatePttLoop(
            pttStateMachine, audioService, pttController, textSender, inputHandler,
            requireConfirmBeforeSend: _cfg.RequireConfirmBeforeSend,
            appendToInput: _cfg.AppendTranscriptionToInput,
            shellInputHandler: _shellHost.InputHandler,
            shellHost: _shellHost);

        return (agentHotkeyService, shellCommands, snapshotCleaner, pttLoop);
    }
}

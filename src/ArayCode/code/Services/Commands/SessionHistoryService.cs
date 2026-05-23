using ArayCode.Services;
using ArayCode.Services.Themes;

namespace ArayCode.Services.Commands;

/// <summary>
/// Shared service for fetching and printing session history.
/// Extracted from <see cref="AgentSwitchingCommands"/> so multiple commands
/// and the hotkey service can reuse the same logic without duplication.
/// </summary>
public sealed class SessionHistoryService
{
    private readonly IStreamShellHost _host;
    private readonly IGatewayService _gatewayService;
    private readonly IColorConsole _console;
    private readonly IPttStateMachine _pttStateMachine;
    private readonly AppConfig _appConfig;

    public SessionHistoryService(
        IStreamShellHost host,
        IGatewayService gatewayService,
        IColorConsole console,
        IPttStateMachine pttStateMachine,
        AppConfig appConfig)
    {
        _host = host;
        _gatewayService = gatewayService;
        _console = console;
        _pttStateMachine = pttStateMachine;
        _appConfig = appConfig;
    }

    /// <summary>Fetches and displays recent session history for the given session key.</summary>
    public async Task PrintSessionHistoryAsync(string sessionKey, CancellationToken ct = default)
    {
        var history = await _gatewayService.FetchSessionHistoryAsync(sessionKey, limit: _appConfig.HistoryDisplayCount);

        // Always show agent introduction, even if history is empty
        if (history != null && history.Count > 0)
        {
            _pttStateMachine.DuringReplay = true;
            try
            {
                _host.AddMessage("");
                _host.AddMessage($"  [{ThemeProvider.Current.Tools.HeaderStyle}]────── previous messages ──────[/]");
                _host.AddMessage("");
                foreach (var entry in history)
                {
                    if (entry.Role.Equals("user", StringComparison.OrdinalIgnoreCase))
                        _console.PrintUserMessage(entry.Content);
                    else
                        _gatewayService.DisplayHistoryEntry(entry);
                }

                var lastEntry = history.LastOrDefault();
                if (lastEntry?.CreatedAt != null)
                {
                    var ago = DateTime.UtcNow - lastEntry.CreatedAt.Value.ToUniversalTime();
                    string agoText;
                    if (ago.TotalMinutes < 1)
                        agoText = "just now";
                    else if (ago.TotalMinutes < 60)
                        agoText = $"{(int)ago.TotalMinutes}m ago";
                    else if (ago.TotalHours < 24)
                        agoText = $"{(int)ago.TotalHours}h {(int)(ago.TotalMinutes % 60)}m ago";
                    else
                        agoText = $"{(int)ago.TotalDays}d ago";
                    _host.AddMessage($"  [{ThemeProvider.Current.Tools.General.Muted}]Last message: {agoText}[/]");
                }
                _host.AddMessage("");
            }
            catch (Exception ex)
            {
                _console.PrintError($"History playback failed: {ex.Message}, StackTrace: " + ex.StackTrace);
                _host.AddMessage("");
            }
            finally
            {
                _pttStateMachine.DuringReplay = false;
            }
        }

        _console.PrintAgentIntroduction(_appConfig);
    }

    /// <summary>
    /// Activates the given agent, persists the selection, and prints session history.
    /// </summary>
    public async Task ActivateWithHistoryAsync(AgentInfo agent, IConfigurationService configService, CancellationToken ct = default)
    {
        AgentRegistry.SetActiveAgent(agent.AgentId);
        _appConfig.LastActiveAgentId = agent.AgentId;
        configService.Save(_appConfig);
        await PrintSessionHistoryAsync(agent.SessionKey, ct);
    }
}

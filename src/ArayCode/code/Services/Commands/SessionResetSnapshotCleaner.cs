namespace ArayCode.Services.Commands;

/// <summary>
/// Mediator that listens for session reset commands (<c>/reset</c>, <c>/new</c>)
/// and clears the stale <see cref="AgentStatusSnapshot"/> for the active session
/// from the <see cref="IAgentActivityStore"/>.
///
/// Wire via <c>shellCommands.CommandExecuted += cleaner.Handle;</c> alongside the
/// conversation naming service subscription.  The handler is a named method so
/// it can be cleanly unsubscribed on dispose.
/// </summary>
public sealed class SessionResetSnapshotCleaner : IDisposable
{
    private readonly IAgentActivityStore? _tracker;
    private readonly EventHandler<CommandExecutedEventArgs> _handler;

    public SessionResetSnapshotCleaner(IAgentActivityStore? tracker)
    {
        _tracker = tracker;
        _handler = OnCommandExecuted;
    }

    /// <summary>
    /// The event handler to subscribe to <c>CommandExecuted</c>.
    /// Exposed so the caller (e.g. AppRunner) can subscribe and unsubscribe.
    /// </summary>
    public EventHandler<CommandExecutedEventArgs> Handle => _handler;

    private void OnCommandExecuted(object? sender, CommandExecutedEventArgs e)
    {
        if (e.Type != ShellCommandType.SessionControl)
            return;

        if (!e.Name.Equals("reset", StringComparison.OrdinalIgnoreCase) &&
            !e.Name.Equals("new", StringComparison.OrdinalIgnoreCase))
            return;

        var sessionKey = AgentRegistry.ActiveSessionKey;
        if (sessionKey != null)
            _tracker?.Reset(sessionKey);
    }

    public void Dispose()
    {
        // The caller unsubscribes via shellCommands.CommandExecuted -= cleaner.Handle.
        // No managed resources to release.
    }
}

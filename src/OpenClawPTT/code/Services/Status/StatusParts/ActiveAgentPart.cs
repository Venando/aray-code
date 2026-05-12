using System.Linq;

namespace OpenClawPTT.Services.StatusParts;

/// <summary>
/// Renders the active agent's emoji icon and display name, e.g. "🌙 Tuon".
/// Data is fed from the agent status snapshot and the global agent registry.
/// </summary>
public sealed class ActiveAgentPart : StatusPartBase
{
    private AgentStatusSnapshot? _snapshot;

    public ActiveAgentPart(DisplayPosition defaultPosition = DisplayPosition.TopSeparatorLeft, int order = 0)
        : base(defaultPosition, order)
    {
    }

    /// <inheritdoc />
    public override string SeparatorBefore => "";

    /// <summary>
    /// Feeds a new status snapshot. The part becomes dirty if the agent
    /// identity (session key, display name) or the registry info changed.
    /// </summary>
    public void Update(AgentStatusSnapshot? snapshot)
    {
        // Snapshot reference changed — mark dirty
        if (!ReferenceEquals(_snapshot, snapshot))
        {
            _snapshot = snapshot;
            MarkDirty();
            return;
        }

        // Re-check on every update anyway — snapshot is a record and may
        // have been replaced with a new instance carrying different values.
        MarkDirty();
    }

    /// <summary>
    /// Forces a re-render when the active session changes in the registry
    /// (e.g. /crew or hotkey switch) even if the snapshot hasn't changed.
    /// </summary>
    public void OnActiveSessionChanged()
    {
        MarkDirty();
    }

    protected override void BuildText()
    {
        if (_snapshot is null)
            return;

        var sessionKey = _snapshot.SessionKey;
        if (string.IsNullOrEmpty(sessionKey))
            return;

        // Look up registry info
        var registryAgent = AgentRegistry.Agents?.FirstOrDefault(a => a.SessionKey == sessionKey);

        // Agent emoji
        string? emoji = null;
        if (registryAgent is not null)
        {
            emoji = TryGetPersistedEmoji(registryAgent.AgentId);
        }
        Builder.Append(emoji ?? "🤖");
        Builder.Append(' ');

        // Agent name with optional color
        if (registryAgent is not null)
        {
            var color = TryGetPersistedColor(registryAgent.AgentId);
            if (!string.IsNullOrWhiteSpace(color))
            {
                Builder.Append('[');
                Builder.Append(color);
                Builder.Append(']');
                Builder.Append(registryAgent.Name);
                Builder.Append("[/]");
            }
            else
            {
                Builder.Append(registryAgent.Name);
            }
        }
        else
        {
            Builder.Append(!string.IsNullOrEmpty(_snapshot.DisplayName) ? _snapshot.DisplayName : "Agent");
        }

        // Status emoji
        Builder.Append(' ');
        Builder.Append(_snapshot.GetStatusEmoji());
        Builder.Append(' ');
    }

    private static string? TryGetPersistedEmoji(string agentId)
    {
        try { return AgentSettingsPersistenceLegacy.GetPersistedEmoji(agentId); }
        catch (InvalidOperationException) { return null; }
    }

    private static string? TryGetPersistedColor(string agentId)
    {
        try { return AgentSettingsPersistenceLegacy.GetPersistedColor(agentId); }
        catch (InvalidOperationException) { return null; }
    }
}

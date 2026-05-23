using System.Linq;

namespace ArayCode.Services.StatusParts;

/// <summary>
/// Renders the active agent's emoji icon and display name, e.g. "🌙 Tuon".
/// Data is fed from the activity store via <see cref="StatusService"/>.
/// </summary>
public sealed class ActiveAgentPart : StatusPartBase
{
    private SessionStateEvent? _state;
    private IAgentActivityStore? _store;
    private string? _lastRenderedKey;
    private string? _lastRenderedDisplayName;

    public ActiveAgentPart(DisplayPosition defaultPosition = DisplayPosition.TopSeparatorLeft, int order = 0)
        : base(defaultPosition, order)
    {
    }

    public override string SeparatorBefore => "";

    /// <summary>
    /// Feeds a new session state and the activity store for status-emoji queries.
    /// </summary>
    public void Update(SessionStateEvent? state, IAgentActivityStore store)
    {
        _store = store;

        if (!ReferenceEquals(_state, state))
        {
            _state = state;
            MarkDirty();
            return;
        }

        if (state is null) return;

        bool changed = false;
        if (state.SessionKey != _lastRenderedKey)
            changed = true;
        else if (state.DisplayName != _lastRenderedDisplayName)
            changed = true;
        else if (store.GetStatusEmoji(state.SessionKey) != GetLastStatusEmoji())
            changed = true;

        if (!changed && state.SessionKey is not null)
        {
            var registryAgent = AgentRegistry.Agents?.FirstOrDefault(a => a.SessionKey == state.SessionKey);
            if (registryAgent is not null)
            {
                var emoji = TryGetPersistedEmoji(registryAgent.AgentId);
                var color = TryGetPersistedColor(registryAgent.AgentId);
                changed = HasRegistryInfoChanged(registryAgent.AgentId, emoji, color);
            }
        }

        if (changed) MarkDirty();
    }

    public void OnActiveSessionChanged() => MarkDirty();

    protected override void BuildText()
    {
        if (_state is null || _store is null) return;

        var sessionKey = _state.SessionKey;
        if (string.IsNullOrEmpty(sessionKey)) return;

        var registryAgent = AgentRegistry.Agents?.FirstOrDefault(a => a.SessionKey == sessionKey);

        string? emoji = null;
        if (registryAgent is not null)
            emoji = TryGetPersistedEmoji(registryAgent.AgentId);
        Builder.Append(emoji ?? "🤖");
        Builder.Append(' ');

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
            Builder.Append(!string.IsNullOrEmpty(_state.DisplayName) ? _state.DisplayName : "Agent");
        }

        Builder.Append(' ');
        Builder.Append(_store.GetStatusEmoji(sessionKey));

        _lastRenderedKey = sessionKey;
        _lastRenderedDisplayName = _state.DisplayName;
        CacheStatusEmoji(_store.GetStatusEmoji(sessionKey));
        if (registryAgent is not null)
            CacheRegistryInfo(registryAgent.AgentId,
                TryGetPersistedEmoji(registryAgent.AgentId),
                TryGetPersistedColor(registryAgent.AgentId));
    }

    private string? _cachedStatusEmoji;
    private string? _cachedRegistryAgentId;
    private string? _cachedRegistryEmoji;
    private string? _cachedRegistryColor;

    private string? GetLastStatusEmoji() => _cachedStatusEmoji;
    private void CacheStatusEmoji(string emoji) => _cachedStatusEmoji = emoji;

    private bool HasRegistryInfoChanged(string agentId, string? emoji, string? color)
    {
        if (_cachedRegistryAgentId != agentId) return true;
        if (_cachedRegistryEmoji != emoji) return true;
        if (_cachedRegistryColor != color) return true;
        return false;
    }

    private void CacheRegistryInfo(string agentId, string? emoji, string? color)
    {
        _cachedRegistryAgentId = agentId;
        _cachedRegistryEmoji = emoji;
        _cachedRegistryColor = color;
    }

    private static string? TryGetPersistedEmoji(string? agentId) =>
        agentId is not null ? AgentSettingsPersistenceLegacy.GetPersistedEmoji(agentId) : null;

    private static string? TryGetPersistedColor(string? agentId) =>
        agentId is not null ? AgentSettingsPersistenceLegacy.GetPersistedColor(agentId) : null;
}

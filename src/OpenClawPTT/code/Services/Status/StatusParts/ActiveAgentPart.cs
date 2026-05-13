using System.Linq;

namespace OpenClawPTT.Services.StatusParts;

/// <summary>
/// Renders the active agent's emoji icon and display name, e.g. "🌙 Tuon".
/// Data is fed from the agent status snapshot and the global agent registry.
/// </summary>
public sealed class ActiveAgentPart : StatusPartBase
{
    private AgentStatusSnapshot? _snapshot;
    private string? _lastRenderedKey;     // tracks session key for dirty detection
    private string? _lastRenderedDisplayName;

    public ActiveAgentPart(DisplayPosition defaultPosition = DisplayPosition.TopSeparatorLeft, int order = 0)
        : base(defaultPosition, order)
    {
    }

    /// <inheritdoc />
    public override string SeparatorBefore => "";

    /// <summary>
    /// Feeds a new status snapshot. Marks dirty only when the agent identity
    /// (session key, display name) or registry info actually changed, preserving
    /// the caching that <see cref="StatusPartBase"/> provides.
    /// </summary>
    public void Update(AgentStatusSnapshot? snapshot)
    {
        // Snapshot reference changed — always mark dirty
        if (!ReferenceEquals(_snapshot, snapshot))
        {
            _snapshot = snapshot;
            MarkDirty();
            return;
        }

        if (snapshot is null)
        {
            // Both null — nothing changed
            return;
        }

        // Both non-null — check if anything we render actually changed
        bool changed = false;

        if (snapshot.SessionKey != _lastRenderedKey)
        {
            changed = true;
        }
        else if (snapshot.DisplayName != _lastRenderedDisplayName)
        {
            changed = true;
        }
        else if (snapshot.GetStatusEmoji() != GetLastStatusEmoji())
        {
            changed = true;
        }

        // Also check if registry info (emoji, color) changed — only when session key is stable
        if (!changed && snapshot.SessionKey is not null)
        {
            var registryAgent = AgentRegistry.Agents?.FirstOrDefault(a => a.SessionKey == snapshot.SessionKey);
            if (registryAgent is not null)
            {
                var emoji = TryGetPersistedEmoji(registryAgent.AgentId);
                var color = TryGetPersistedColor(registryAgent.AgentId);
                changed = HasRegistryInfoChanged(registryAgent.AgentId, emoji, color);
            }
        }

        if (changed)
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

        // Cache current render values for dirty detection on next Update()
        _lastRenderedKey = sessionKey;
        _lastRenderedDisplayName = _snapshot.DisplayName;
        CacheStatusEmoji(_snapshot.GetStatusEmoji());
        if (registryAgent is not null)
            CacheRegistryInfo(registryAgent.AgentId,
                TryGetPersistedEmoji(registryAgent.AgentId),
                TryGetPersistedColor(registryAgent.AgentId));
    }

    // ── Cached value tracking for dirty detection ────────────────────────

    private string? _cachedStatusEmoji;
    private string? _cachedRegistryAgentId;
    private string? _cachedRegistryEmoji;
    private string? _cachedRegistryColor;

    private string? GetLastStatusEmoji() => _cachedStatusEmoji;
    private void CacheStatusEmoji(string emoji) => _cachedStatusEmoji = emoji;

    private bool HasRegistryInfoChanged(string agentId, string? emoji, string? color)
    {
        if (_cachedRegistryAgentId != agentId)
            return true;
        if (_cachedRegistryEmoji != emoji)
            return true;
        if (_cachedRegistryColor != color)
            return true;
        return false;
    }

    private void CacheRegistryInfo(string agentId, string? emoji, string? color)
    {
        _cachedRegistryAgentId = agentId;
        _cachedRegistryEmoji = emoji;
        _cachedRegistryColor = color;
    }

    // ── Persistence helpers ─────────────────────────────────────────────

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

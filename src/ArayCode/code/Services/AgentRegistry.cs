using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArayCode.ConfigWizard;
using ArayCode.Services;
using ArayCode.Services.Commands;

namespace ArayCode;

/// <summary>
/// Fallback lock-free helpers for AgentRegistry.
/// </summary>
internal static class AgentRegistryHelpers
{
    /// <summary>Returns the first agent matching the given session key, or null.</summary>
    public static AgentInfo? FindBySessionKey(List<AgentInfo> agents, string? sessionKey)
    {
        return sessionKey != null
            ? agents.FirstOrDefault(a => a.SessionKey == sessionKey)
            : null;
    }

    /// <summary>Returns the default agent, or first available, or null.</summary>
    public static AgentInfo? GetDefaultOrFirst(List<AgentInfo> agents)
    {
        return agents.FirstOrDefault(a => a.IsDefault) ?? agents.FirstOrDefault();
    }

    /// <summary>Returns the active agent info, or null.</summary>
    public static AgentInfo? GetActiveAgent(List<AgentInfo> agents, string? activeSessionKey)
    {
        return FindBySessionKey(agents, activeSessionKey);
    }
}

/// <summary>
/// Thread-safe registry of available agents and the currently active session key.
/// Accessible app-wide as a singleton. Concerns: agent list management and
/// active session tracking. Persisted settings (hotkey, emoji) are managed
/// by <see cref="IAgentSettingsPersistence"/>.
/// </summary>
public static class AgentRegistry
{
    private static readonly object _lock = new();

    // ── Agent list ──
    private static List<AgentInfo> _agents = new();

    // ── Active session ──
    private static string? _activeSessionKey;

    /// <summary>Event raised when the active session changes.</summary>
    public static event Action<string?>? ActiveSessionChanged;

    /// <summary>Replaces the entire agent list. Resets active session if no longer valid.</summary>
    public static void SetAgents(IReadOnlyList<AgentInfo> agents)
    {
        string? newSessionKey;
        string? previousSessionKey;
        lock (_lock)
        {
            previousSessionKey = _activeSessionKey;
            _agents = agents.ToList();

            // Validate current active session
            if (_activeSessionKey != null && _agents.All(a => a.SessionKey != _activeSessionKey))
                _activeSessionKey = null;

            // Pick default or first if none active
            if (_activeSessionKey == null)
            {
                var defaultAgent = AgentRegistryHelpers.GetDefaultOrFirst(_agents);
                _activeSessionKey = defaultAgent?.SessionKey;
            }

            newSessionKey = _activeSessionKey;
        }

        // Fire event if active session changed (e.g. null → agent after snapshot load)
        if (previousSessionKey != newSessionKey)
            ActiveSessionChanged?.Invoke(newSessionKey);
    }

    /// <summary>All available agents.</summary>
    public static IReadOnlyList<AgentInfo> Agents
    {
        get
        {
            lock (_lock) return _agents.ToList().AsReadOnly();
        }
    }

    /// <summary>Currently active session key. Messages from other sessions are filtered out.</summary>
    public static string? ActiveSessionKey
    {
        get { lock (_lock) return _activeSessionKey; }
    }

    /// <summary>Name of the currently active agent, or null if none.</summary>
    public static string? ActiveAgentName
    {
        get
        {
            lock (_lock)
            {
                var active = AgentRegistryHelpers.FindBySessionKey(_agents, _activeSessionKey);
                return active?.Name;
            }
        }
    }

    /// <summary>AgentId of the currently active agent, or null.</summary>
    public static string? ActiveAgentId
    {
        get
        {
            lock (_lock)
            {
                var active = AgentRegistryHelpers.FindBySessionKey(_agents, _activeSessionKey);
                return active?.AgentId;
            }
        }
    }

    /// <summary>True if a valid agent is currently active.</summary>
    public static bool IsActiveAgentAvailable
    {
        get
        {
            lock (_lock)
            {
                return AgentRegistryHelpers.FindBySessionKey(_agents, _activeSessionKey) != null;
            }
        }
    }

    /// <summary>Switch active agent by agentId.</summary>
    public static bool SetActiveAgent(string agentId)
    {
        // Reject agent switching while any wizard is active (config, agent config, etc.)
        if (WizardState.IsActive)
            return false;

        lock (_lock)
        {
            var agent = _agents.FirstOrDefault(a =>
                a.AgentId.Equals(agentId, StringComparison.OrdinalIgnoreCase));
            if (agent == null) return false;

            if (_activeSessionKey != agent.SessionKey)
            {
                _activeSessionKey = agent.SessionKey;
                ActiveSessionChanged?.Invoke(_activeSessionKey);
            }
            return true;
        }
    }

    /// <summary>
    /// Switches to the agent, persists the choice to config,
    /// and optionally prints recent session history.
    /// Shared by <c>/chat</c> and the agent-status bottom panel (DRY).
    /// </summary>
    public static async Task SwitchToAgentAsync(
        string agentId,
        IConfigurationService configService,
        SessionHistoryService? historyService = null,
        CancellationToken ct = default)
    {
        if (!SetActiveAgent(agentId))
            return;

        var cfg = configService.Load() ?? new AppConfig();
        cfg.LastActiveAgentId = agentId;
        configService.Save(cfg);

        if (historyService is not null)
        {
            var sessionKey = ActiveSessionKey;
            if (sessionKey is not null)
                await historyService.PrintSessionHistoryAsync(sessionKey, ct);
        }
    }

    /// <summary>Switch active agent by session key.</summary>
    public static bool SetActiveSession(string sessionKey)
    {
        lock (_lock)
        {
            if (_agents.All(a => a.SessionKey != sessionKey))
                return false;

            if (_activeSessionKey != sessionKey)
            {
                _activeSessionKey = sessionKey;
                ActiveSessionChanged?.Invoke(_activeSessionKey);
            }
            return true;
        }
    }

    /// <summary>Returns the default agent (marked IsDefault), or the first available, or null.</summary>
    public static AgentInfo? GetDefaultAgent()
    {
        lock (_lock) return AgentRegistryHelpers.GetDefaultOrFirst(_agents);
    }

    /// <summary>Deactivates the active agent (sets active session to null).</summary>
    public static void Deactivate()
    {
        lock (_lock)
        {
            if (_activeSessionKey != null)
            {
                _activeSessionKey = null;
                ActiveSessionChanged?.Invoke(null);
            }
        }
    }

    /// <summary>
    /// Returns true if the message's sessionKey matches the currently active session.
    /// System messages (null sessionKey) always pass. When no agent is active,
    /// agent messages (non-null sessionKey) are suppressed.
    /// </summary>
    public static bool IsMessageForActiveSession(string? sessionKey)
    {
        if (sessionKey == null) return true;
        lock (_lock) return _activeSessionKey != null && sessionKey == _activeSessionKey;
    }

    /// <summary>Gets the active agent's display name and emoji via the legacy bridge.</summary>
    public static void GetActiveNameAndEmoji(out object agentName, out object emoji, string defaultName = "Agent", string defaultEmoji = "🤖")
    {
        lock (_lock)
        {
            var active = AgentRegistryHelpers.FindBySessionKey(_agents, _activeSessionKey);
            agentName = active?.Name ?? defaultName;
            emoji = active != null
                ? AgentSettingsPersistenceLegacy.GetPersistedEmoji(active.AgentId) ?? defaultEmoji
                : defaultEmoji;
        }
    }

    /// <summary>Gets the active agent's color via the legacy bridge, or null for default.</summary>
    public static string? GetActiveColor()
    {
        lock (_lock)
        {
            var active = AgentRegistryHelpers.FindBySessionKey(_agents, _activeSessionKey);
            return active != null ? AgentSettingsPersistenceLegacy.GetPersistedColor(active.AgentId) : null;
        }
    }
}

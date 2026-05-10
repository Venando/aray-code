using System;
using System.Linq;
using System.Text;

namespace OpenClawPTT.Services;

/// <summary>
/// Tracks gateway, TTS, and agent status, rendering a compact status line
/// on both sides of the StreamShell top separator.
///
/// Right side: "  GW:[color]● label[/]  TTS:[color]● label[/]"
/// Left side:  "🤖 Name 🟢 model · thinking · 5% (12k/200k)"
///
/// When no <see cref="IAgentStatusTracker"/> is provided, the left side is empty.
/// Thread-safe: all public methods synchronize on a lock before mutating state.
/// Subscribes to the tracker's <see cref="IAgentStatusTracker.Changed"/> event
/// and triggers a re-render whenever a snapshot updates.
/// </summary>
public sealed class StatusService : IStatusService, IDisposable
{
    private readonly IStreamShellHost _shellHost;
    private IAgentStatusTracker? _agentTracker;
    private readonly object _lock = new();
    private readonly StringBuilder _sb = new(192);

    private string _gatewayLabel = "Starting";
    private StatusColor _gatewayColor = StatusColor.Yellow;
    private string _ttsLabel = "Starting";
    private StatusColor _ttsColor = StatusColor.Yellow;

    public StatusService(IStreamShellHost shellHost, IAgentStatusTracker? agentStatusTracker = null)
    {
        _shellHost = shellHost ?? throw new ArgumentNullException(nameof(shellHost));
        _agentTracker = agentStatusTracker;

        if (_agentTracker != null)
            _agentTracker.Changed += OnAgentStatusChanged;
    }

    public void SetGatewayStatus(string label, StatusColor color)
    {
        lock (_lock)
        {
            _gatewayLabel = label;
            _gatewayColor = color;
            Render();
        }
    }

    public void SetTtsStatus(string label, StatusColor color)
    {
        lock (_lock)
        {
            _ttsLabel = label;
            _ttsColor = color;
            Render();
        }
    }

    public void SetAgentStatusTracker(IAgentStatusTracker tracker)
    {
        lock (_lock)
        {
            // Unsubscribe from previous tracker if any
            if (_agentTracker != null)
                _agentTracker.Changed -= OnAgentStatusChanged;

            _agentTracker = tracker;
            _agentTracker.Changed += OnAgentStatusChanged;
            Render();
        }
    }

    /// <summary>
    /// Called when the agent status tracker fires its Changed event.
    /// Re-renders the top separator with updated agent info on the left.
    /// </summary>
    private void OnAgentStatusChanged()
    {
        lock (_lock) { Render(); }
    }

    private void Render()
    {
        try
        {
            string rightText = $"  GW:[{ToMarkupColor(_gatewayColor)}]● {_gatewayLabel}[/]" +
                               $"  TTS:[{ToMarkupColor(_ttsColor)}]● {_ttsLabel}[/]";
            string leftText = BuildLeftText();
            _shellHost.SetTopSeparator(leftText: leftText, rightText: rightText, repeatedCharacter: '─');
        }
        catch (Exception ex)
        {
            // Rendering is best-effort — never crash the caller if shell is disposed
            System.Diagnostics.Debug.WriteLine($"StatusService.Render failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds the left-side agent status text from the active agent's snapshot.
    /// Returns empty string when no agent is connected or no tracker is available.
    /// </summary>
    private string BuildLeftText()
    {
        if (_agentTracker == null)
            return string.Empty;

        // Use AgentRegistry to find the currently active agent's session key
        // rather than GetMainAgent() which just returns the first non-subagent.
        var activeSessionKey = AgentRegistry.ActiveSessionKey;
        if (string.IsNullOrEmpty(activeSessionKey))
            return string.Empty;

        var mainAgent = _agentTracker.Get(activeSessionKey);
        if (mainAgent == null)
            return string.Empty;

        _sb.Clear();

        // Agent icon emoji + name
        AppendAgentEmojiAndName(mainAgent);

        // Status emoji (🟢 running, ✅ done, 🔄 tool, etc.)
        _sb.Append(' ');
        _sb.Append(mainAgent.GetStatusEmoji());

        // Model
        if (!string.IsNullOrEmpty(mainAgent.Model))
        {
            _sb.Append(' ');
            _sb.Append(ShortenModelName(mainAgent.Model));
        }

        // Thinking level
        if (!string.IsNullOrEmpty(mainAgent.ThinkingDefault))
        {
            _sb.Append(" · ");
            _sb.Append(mainAgent.ThinkingDefault);
        }

        // Token usage: percentage (current/max)
        AppendTokenUsage(mainAgent);

        return _sb.ToString();
    }

    private void AppendAgentEmojiAndName(AgentStatusSnapshot agent)
    {
        var registryAgent = AgentRegistry.Agents.FirstOrDefault(a => a.SessionKey == agent.SessionKey);
        string emoji;
        string name;

        if (registryAgent != null)
        {
            emoji = TryGetPersistedEmoji(registryAgent.AgentId) ?? "🤖";
            name = registryAgent.Name;
        }
        else
        {
            emoji = "🤖";
            name = !string.IsNullOrEmpty(agent.DisplayName) ? agent.DisplayName : "Agent";
        }

        _sb.Append(emoji);
        _sb.Append(' ');
        _sb.Append(name);
    }

    private void AppendTokenUsage(AgentStatusSnapshot agent)
    {
        long? maxContext = agent.ContextTokens;
        long? currentTokens = agent.TotalTokens ?? agent.InputTokens;

        if (maxContext == null || maxContext <= 0 || currentTokens == null || currentTokens <= 0)
            return;

        double percent = (double)currentTokens.Value / maxContext.Value * 100.0;

        _sb.Append(" · ");

        if (percent < 10.0)
            _sb.AppendFormat("{0:F1}%", percent);
        else
            _sb.AppendFormat("{0:F0}%", percent);

        _sb.Append(" (");
        _sb.Append(FormatTokenCount(currentTokens.Value));
        _sb.Append('/');
        _sb.Append(FormatTokenCount(maxContext.Value));
        _sb.Append(')');
    }

    /// <summary>
    /// Shortens model names by removing the common provider prefix for compact display.
    /// E.g. "deepseek/deepseek-v4-flash" → "deepseek-v4-flash"
    ///       "anthropic/claude-opus-4-6" → "claude-opus-4-6"
    ///       "kimi/kimi-k2.6" → "kimi-k2.6"
    /// </summary>
    private static string ShortenModelName(string model)
    {
        if (string.IsNullOrEmpty(model))
            return model;

        int slashIndex = model.IndexOf('/');
        if (slashIndex > 0 && slashIndex < model.Length - 1)
        {
            // Option to keep provider prefix when it differs from model name
            var prefix = model[..slashIndex];
            var name = model[(slashIndex + 1)..];
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return name;
            // Keep the full model string; it's short enough
            if (model.Length <= 30)
                return model;
            return name;
        }
        return model;
    }

    /// <summary>
    /// Formats a token count to a human-readable short form:
    /// e.g. 12000 → "12k", 264000 → "264k", 1000000 → "1M"
    /// </summary>
    private static string FormatTokenCount(long count)
    {
        if (count >= 1_000_000)
            return $"{(double)count / 1_000_000:F1}M".Replace(",", ".");
        if (count >= 1_000)
            return $"{(double)count / 1_000:F0}k".Replace(",", ".");
        return count.ToString();
    }

    /// <summary>Maps <see cref="StatusColor"/> to Spectre.Console markup color names.</summary>
    private static string ToMarkupColor(StatusColor color) => color switch
    {
        StatusColor.Green => "green",
        StatusColor.Yellow => "yellow",
        StatusColor.Red => "red",
        _ => "yellow",
    };

    /// <summary>
    /// Safely attempts to get a persisted emoji for an agent, returning null
    /// if the <see cref="AgentSettingsPersistenceLegacy"/> bridge hasn't been
    /// initialized yet (e.g. during early startup or in unit tests).
    /// </summary>
    private static string? TryGetPersistedEmoji(string agentId)
    {
        try
        {
            return AgentSettingsPersistenceLegacy.GetPersistedEmoji(agentId);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_agentTracker != null)
            _agentTracker.Changed -= OnAgentStatusChanged;
    }
}

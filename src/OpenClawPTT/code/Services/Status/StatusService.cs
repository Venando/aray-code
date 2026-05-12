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
    private const string RepeatedCharacterMarkup = "white";
    private const string ConversationNameMarkup = $"italic {RepeatedCharacterMarkup}";

    // Pre-baked constant markup fragments — computed once, never re-allocated
    private const string GwPrefix = " GW:[";
    private const string TtsPrefix = "]● ";
    private const string TtsSuffix = "[/] TTS:[";
    private const string RightSuffix = "[/]";
    private const string ConvOpen = "[grey]\u2502[/] [" + ConversationNameMarkup + "]";
    private const string ConvClose = "[/] [grey]\u2502[/]";
    private const string Separator = "──────────────── ";

    private readonly IStreamShellHost _shellHost;
    private IAgentStatusTracker? _agentTracker;
    private readonly object _lock = new();
    private readonly StringBuilder _sb = new(256); // left side
    private readonly StringBuilder _sbRight = new(128); // right side — reused across calls

    private string _gatewayLabel = "Starting";
    private StatusColor _gatewayColor = StatusColor.Yellow;
    private string _ttsLabel = "Starting";
    private StatusColor _ttsColor = StatusColor.Yellow;
    private string? _conversationName;

    public StatusService(IStreamShellHost shellHost, IAgentStatusTracker? agentStatusTracker = null)
    {
        _shellHost = shellHost ?? throw new ArgumentNullException(nameof(shellHost));
        _agentTracker = agentStatusTracker;

        if (_agentTracker != null)
            _agentTracker.Changed += OnAgentStatusChanged;

        // React to agent switching in the registry (e.g. /crew or hotkey switch)
        AgentRegistry.ActiveSessionChanged += OnActiveSessionChanged;
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

    public void SetConversationName(string? name)
    {
        lock (_lock)
        {
            _conversationName = name;
            Render();
        }
    }

    /// <summary>
    /// Called when the active agent session changes in the registry.
    /// Triggers a re-render so the left-side agent info reflects the switched agent.
    /// </summary>
    private void OnActiveSessionChanged(string? _)
    {
        lock (_lock) { Render(); }
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
            // Build right side into reusable StringBuilder, then ToString() once
            _sbRight.Clear();
            _sbRight.Append(GwPrefix);
            _sbRight.Append(ToMarkupColor(_gatewayColor));
            _sbRight.Append(TtsPrefix);
            _sbRight.Append(_gatewayLabel);
            _sbRight.Append(TtsSuffix);
            _sbRight.Append(ToMarkupColor(_ttsColor));
            _sbRight.Append(TtsPrefix);
            _sbRight.Append(_ttsLabel);
            _sbRight.Append(RightSuffix);

            string rightText = _sbRight.ToString(); // one alloc per render
            string leftText = BuildLeftText();
            _shellHost.SetTopSeparator(leftText: leftText, rightText: rightText,
                repeatedCharacter: '─', repeatedCharMarkup: RepeatedCharacterMarkup);
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
        _sb.Append(Separator);

        // Agent icon emoji + name
        AppendAgentEmojiAndName(mainAgent);

        // Status emoji (🟢 running, ✅ done, 🔄 tool, etc.)
        _sb.Append(' ');
        _sb.Append(mainAgent.GetStatusEmoji());
        _sb.Append(' ');

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
        AppendConversationName();
        _sb.Append(' ');

        return _sb.ToString();
    }

    private void AppendAgentEmojiAndName(AgentStatusSnapshot agent)
    {
        var registryAgent = AgentRegistry.Agents.FirstOrDefault(a => a.SessionKey == agent.SessionKey);

        if (registryAgent != null)
        {
            _sb.Append(TryGetPersistedEmoji(registryAgent.AgentId) ?? "🤖");
            _sb.Append(' ');
            var color = TryGetPersistedColor(registryAgent.AgentId);
            if (!string.IsNullOrWhiteSpace(color))
            {
                _sb.Append('[');
                _sb.Append(color); // no interpolation — direct append
                _sb.Append(']');
                _sb.Append(registryAgent.Name);
                _sb.Append("[/]");
            }
            else
            {
                _sb.Append(registryAgent.Name);
            }
        }
        else
        {
            _sb.Append("🤖 ");
            _sb.Append(!string.IsNullOrEmpty(agent.DisplayName) ? agent.DisplayName : "Agent");
        }
    }

    private void AppendTokenUsage(AgentStatusSnapshot agent)
    {
        long? maxContext = agent.ContextTokens;
        long? currentTokens = agent.TotalTokens ?? agent.InputTokens;

        if (maxContext == null || maxContext <= 0 || currentTokens == null || currentTokens <= 0)
            return;

        double percent = (double)currentTokens.Value / maxContext.Value * 100.0;

        _sb.Append(" · ");

        // AppendFormat boxes the double args — use manual formatting instead
        if (percent < 10.0)
            AppendDouble(_sb, percent, 1);
        else
            AppendDouble(_sb, percent, 0);
        _sb.Append('%');

        _sb.Append(" (");
        AppendTokenCount(_sb, currentTokens.Value);
        _sb.Append('/');
        AppendTokenCount(_sb, maxContext.Value);
        _sb.Append(')');
    }

    /// <summary>
    /// Appends a double to <paramref name="sb"/> with the given decimal places,
    /// without boxing or allocating a temporary string.
    /// Works for values in the range [0, 999.9] — sufficient for percentages.
    /// </summary>
    private static void AppendDouble(StringBuilder sb, double value, int decimals)
    {
        // Round before splitting to avoid e.g. 9.999 printing as "10.0%"
        long scale = decimals == 0 ? 1 : 10;
        long rounded = (long)Math.Round(value * scale, MidpointRounding.AwayFromZero);

        long intPart = rounded / scale;
        long fracPart = rounded % scale;

        sb.Append(intPart); // long overload — no boxing
        if (decimals > 0)
        {
            sb.Append('.');
            sb.Append(fracPart);
        }
    }

    /// <summary>
    /// Appends a human-readable token count (12k, 264k, 1.1M) to <paramref name="sb"/>
    /// without allocating a temporary string.
    /// </summary>
    private static void AppendTokenCount(StringBuilder sb, long count)
    {
        if (count >= 1_000_000)
        {
            AppendDouble(sb, (double)count / 1_000_000, 1);
            sb.Append('M');
        }
        else if (count >= 1_000)
        {
            AppendDouble(sb, (double)count / 1_000, 0);
            sb.Append('k');
        }
        else
        {
            sb.Append(count);
        }
    }

    /// <summary>For testing: returns human-readable token count as a string.</summary>
    internal static string FormatTokenCount(long count)
    {
        var sb = new StringBuilder(16);
        AppendTokenCount(sb, count);
        return sb.ToString();
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

    private void AppendConversationName()
    {
        if (string.IsNullOrWhiteSpace(_conversationName))
            return;

        _sb.Append(ConvOpen); // pre-baked constant, no alloc
        _sb.Append(_conversationName);
        _sb.Append(ConvClose); // pre-baked constant, no alloc
    }

    private static string? TryGetPersistedColor(string agentId)
    {
        try
        {
            return AgentSettingsPersistenceLegacy.GetPersistedColor(agentId);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        AgentRegistry.ActiveSessionChanged -= OnActiveSessionChanged;

        if (_agentTracker != null)
            _agentTracker.Changed -= OnAgentStatusChanged;
    }
}

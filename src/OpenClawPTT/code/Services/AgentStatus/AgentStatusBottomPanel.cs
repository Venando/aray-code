using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;
using StreamShell;

namespace OpenClawPTT.Services;

/// <summary>
/// StreamShell bottom panel that displays all agents and their subagent statuses.
/// Fixed at 5 lines high.
/// - Lines 1-3: Subagent groups — one row per main agent that has active subagents.
///   Format: "🎩: ⏳ │ ⏳ │ 🟢" (parent emoji + colon + subagent status emojis)
///   Each row is centered.
/// - Line 4: All main agents centered, showing emoji, name, color, and status emoji.
/// </summary>
public sealed class AgentStatusBottomPanel : IBottomPanel
{
    private const int LineCountValue = 7;
    private readonly IAgentStatusTracker _tracker;
    private bool _isDirty;
    private readonly IColorConsole _colorConsole;
    private readonly string[] _lines;
    private int _lastConsoleWidth = -1;
    private int _lastDisplayFingerprint;
    private readonly StringBuilder _sb = new(256);

    public AgentStatusBottomPanel(IAgentStatusTracker tracker, IColorConsole colorConsole)
    {
        _tracker = tracker;
        _colorConsole = colorConsole;
        _tracker.Changed += OnTrackerChanged;
        _lines = new string[LineCountValue];
        // Prime the fingerprint so the first tracker event doesn't double-render.
        _lastDisplayFingerprint = ComputeDisplayFingerprint();
    }

    private void OnTrackerChanged()
    {
        var fingerprint = ComputeDisplayFingerprint();
        if (fingerprint != _lastDisplayFingerprint)
        {
            _lastDisplayFingerprint = fingerprint;
            _isDirty = true;
        }
    }

    public int LineCount => LineCountValue;
    public bool IsDirty => _isDirty;
    public void ClearDirty() => _isDirty = false;

    public IReadOnlyList<string> GetLines(string currentInput)
    {
        int currentWidth;
        try { currentWidth = ConsoleMetrics.GetWindowWidth(); }
        catch { currentWidth = _lastConsoleWidth; }

        if (!_isDirty && currentWidth == _lastConsoleWidth)
            return _lines;

        _lastConsoleWidth = currentWidth;

        var all = _tracker.All;
        var mainAgents = all.Where(s => !s.IsSubagent && AgentRegistry.Agents.Any(a => a.SessionKey == s.SessionKey)).ToList();
        // Show ALL subagents (active + recently finished), filtering only those
        // that finished more than 30 seconds ago to keep the panel informative.
        var activeSubs = all.Where(s => s.IsSubagent && !ShouldHideSubagent(s)).ToList();

        // Group subagents by parent
        var subagentGroups = activeSubs
            .GroupBy(s => s.ParentSessionKey ?? "")
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .ToList();

        int rowIndex = 0;

        _sb.Clear();
        AddRow(ref rowIndex, BuildCenteredMainAgentsLine(mainAgents, _sb));
        AddRow(ref rowIndex, "");

        for (int i = 0; i < subagentGroups.Count; i++)
        {
            _sb.Clear();
            AddRow(ref rowIndex, BuildSubagentGroupLine(subagentGroups[i], mainAgents, _sb));
            AddRow(ref rowIndex, "");
        }

        ClearRows(rowIndex);
        _isDirty = false;

        return _lines;
    }

    private void AddRow(ref int rowIndex, string row)
    {
        _lines[LineCountValue - ++rowIndex] = row;
    }

    private void ClearRows(int rowIndex)
    {
        while (rowIndex != LineCountValue)
            AddRow(ref rowIndex, string.Empty);
    }

    // ─────────────────────────────────────────────────────────────────
    // Subagent group line (centered)
    // Format: "🎩: ⏳ │ ⏳ │ 🟢"
    // ─────────────────────────────────────────────────────────────────

    private static string BuildSubagentGroupLine(IGrouping<string, AgentStatusSnapshot> group, List<AgentStatusSnapshot> mainAgents, StringBuilder sb)
    {
        var parent = mainAgents.FirstOrDefault(m => m.SessionKey == group.Key);
        var (parentEmoji, _, _, parentShow) = GetAgentDisplayInfo(parent);
        if (!parentShow)
            return string.Empty;

        sb.Append(parentEmoji);
        sb.Append(": ");

        bool first = true;
        foreach (var sub in group)
        {
            if (!first)
                sb.Append(" │ ");
            first = false;
            sb.Append(sub.GetStatusEmoji());
        }

        return CenterMarkup(sb.ToString());
    }

    // ─────────────────────────────────────────────────────────────────
    // Main agents line (centered)
    // Format: "🎩 🟢 │ 🦊 🟢 │ 🤖 ⚪"
    // ─────────────────────────────────────────────────────────────────

    private static string BuildCenteredMainAgentsLine(List<AgentStatusSnapshot> mainAgents, StringBuilder sb)
    {
        if (mainAgents.Count == 0)
            return string.Empty;

        bool first = true;
        foreach (var agent in mainAgents)
        {
            var (emoji, _, _, show) = GetAgentDisplayInfo(agent);
            if (!show)
                continue;

            if (!first)
                sb.Append(" [grey]│[/] ");
            first = false;

            var statusEmoji = agent.GetStatusEmoji();
            sb.Append(emoji);
            sb.Append(' ');
            sb.Append(statusEmoji);
        }

        if (first) // nothing visible
            return string.Empty;

        return CenterMarkup(sb.ToString());
    }

    private int ComputeDisplayFingerprint()
    {
        var hash = new HashCode();
        foreach (var s in _tracker.All)
        {
            hash.Add(s.SessionKey);
            hash.Add(s.IsSubagent);
            hash.Add(s.ParentSessionKey);
            hash.Add(s.Status);
            hash.Add(s.StopReason);
            hash.Add(s.SubagentRunState);
            hash.Add(s.HasActiveSubagentRun);
            hash.Add(s.Phase);
            hash.Add(s.EndedAt);
            hash.Add(s.AbortedLastRun);
            hash.Add(s.ChildSessions.Count);
            foreach (var c in s.ChildSessions) hash.Add(c);

            var registryAgent = AgentRegistry.Agents.FirstOrDefault(a => a.SessionKey == s.SessionKey);
            if (registryAgent != null)
            {
                hash.Add(AgentSettingsPersistenceLegacy.GetPersistedEmoji(registryAgent.AgentId));
                hash.Add(AgentSettingsPersistenceLegacy.GetPersistedShowInStatusPanel(registryAgent.AgentId));
            }
        }
        return hash.ToHashCode();
    }

    // ─────────────────────────────────────────────────────────────────
    // Centering helper
    // ─────────────────────────────────────────────────────────────────

    private static string CenterMarkup(string markup)
    {
        try
        {
            int consoleWidth = ConsoleMetrics.GetWindowWidth();
            int visibleLen = GetVisibleLength(markup);
            int padding = Math.Max(0, (consoleWidth - visibleLen) / 2);
            return new string(' ', padding) + markup;
        }
        catch
        {
            return markup;
        }
    }

    /// <summary>
    /// Computes the visible character length of a Spectre markup string.
    /// Markup tags ([color], [/]) count as 0. Escaped brackets ([[, ]]) count as 1.
    /// </summary>
    private static int GetVisibleLength(string markup)
    {
        return Markup.Remove(markup).Length;
    }

    private static bool ShouldHideSubagent(AgentStatusSnapshot s)
    {
        // Hide finished subagents only after a 30-second grace period so the
        // user sees the ✅ completion marker before the row disappears.
        if (!s.IsFinished)
            return false;

        var endedOrUpdated = s.EndedAt ?? s.UpdatedAt;
        if (endedOrUpdated == null)
            return false;

        var elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - endedOrUpdated.Value;
        return elapsed > 30_000; // 30 seconds
    }

    // ─────────────────────────────────────────────────────────────────
    // Agent display info (emoji, color, name) — pulls from AgentRegistry when available
    // ─────────────────────────────────────────────────────────────────

    private static (string emoji, string color, string name, bool showInStatus) GetAgentDisplayInfo(AgentStatusSnapshot? snapshot)
    {
        if (snapshot == null)
            return ("🤖", "grey", "Agent", true);

        // Try to match via AgentRegistry
        var registryAgent = AgentRegistry.Agents.FirstOrDefault(a => a.SessionKey == snapshot.SessionKey);
        if (registryAgent != null)
        {
            var emoji = AgentSettingsPersistenceLegacy.GetPersistedEmoji(registryAgent.AgentId) ?? "🤖";
            var color = AgentSettingsPersistenceLegacy.GetPersistedColor(registryAgent.AgentId) ?? "grey";
            var show = AgentSettingsPersistenceLegacy.GetPersistedShowInStatusPanel(registryAgent.AgentId);
            var name = Markup.Escape(registryAgent.Name);
            return (emoji, color, name, show);
        }

        // Fallback: use snapshot data
        var fallbackName = Markup.Escape(ShortenMainAgentName(snapshot.DisplayName));
        return ("🤖", "grey", fallbackName, true);
    }

    // ─────────────────────────────────────────────────────────────────
    // Name helpers
    // ─────────────────────────────────────────────────────────────────

    private static string ShortenMainAgentName(string? displayName)
    {
        if (string.IsNullOrEmpty(displayName))
            return "Agent";

        // Extract the middle part from patterns like "webchat:g-agent-anime-main"
        if (displayName.Contains("-main", StringComparison.OrdinalIgnoreCase))
        {
            var parts = displayName.Split('-');
            if (parts.Length >= 3)
                return parts[^2]; // e.g. "anime" from "...-agent-anime-main"
        }

        return displayName.Length > 16 ? displayName[..16] : displayName;
    }
}

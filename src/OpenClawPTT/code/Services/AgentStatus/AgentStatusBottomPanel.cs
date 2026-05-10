using System.Text;
using System.Text.RegularExpressions;
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
    private const int LineCountValue = 5;
    private readonly IAgentStatusTracker _tracker;
    private bool _isDirty;

    public AgentStatusBottomPanel(IAgentStatusTracker tracker)
    {
        _tracker = tracker;
        _tracker.Changed += OnTrackerChanged;
    }

    private void OnTrackerChanged() => _isDirty = true;

    public int LineCount => LineCountValue;
    public bool IsDirty => _isDirty;
    public void ClearDirty() => _isDirty = false;

    public IReadOnlyList<string> GetLines(string currentInput)
    {
        var lines = new string[LineCountValue];

        var all = _tracker.All;
        var mainAgents = all.Where(s => !s.IsSubagent).ToList();
        var activeSubs = all.Where(s => s.IsSubagent && !s.IsFinished).ToList();

        // Group subagents by parent
        var subagentGroups = activeSubs
            .GroupBy(s => s.ParentSessionKey ?? "")
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .ToList();

        // Line 0: Tab autocomplete suggestion slot (must be empty)
        lines[0] = string.Empty;

        // Lines 1-3: Subagent groups (one row per parent agent, up to 3)
        int groupIdx = 0;
        for (int line = 1; line <= 3; line++)
        {
            if (groupIdx < subagentGroups.Count)
            {
                lines[line] = BuildSubagentGroupLine(subagentGroups[groupIdx], mainAgents);
                groupIdx++;
            }
            else
            {
                lines[line] = string.Empty;
            }
        }

        // Line 4: All main agents centered
        lines[4] = BuildCenteredMainAgentsLine(mainAgents);

        return lines;
    }

    // ─────────────────────────────────────────────────────────────────
    // Subagent group line (centered)
    // Format: "🎩: ⏳ │ ⏳ │ 🟢"
    // ─────────────────────────────────────────────────────────────────

    private static string BuildSubagentGroupLine(IGrouping<string, AgentStatusSnapshot> group, List<AgentStatusSnapshot> mainAgents)
    {
        var parent = mainAgents.FirstOrDefault(m => m.SessionKey == group.Key);
        var (parentEmoji, parentColor, _, parentShow) = GetAgentDisplayInfo(parent);
        if (!parentShow)
            return string.Empty;

        var sb = new StringBuilder();
        sb.Append(parentEmoji);
        sb.Append(": ");

        var subs = group.ToList();
        for (int i = 0; i < subs.Count; i++)
        {
            if (i > 0)
                sb.Append(" │ ");
            sb.Append(subs[i].GetStatusEmoji());
        }

        return CenterMarkup(sb.ToString());
    }

    // ─────────────────────────────────────────────────────────────────
    // Main agents line (centered)
    // Format: "🎩 Maestro 🟢 │ 🦊 Anime 🟢 │ 🤖 Worker ⚪"
    // ─────────────────────────────────────────────────────────────────

    private static string BuildCenteredMainAgentsLine(List<AgentStatusSnapshot> mainAgents)
    {
        if (mainAgents.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        bool first = true;
        foreach (var agent in mainAgents)
        {
            var (emoji, color, name, show) = GetAgentDisplayInfo(agent);
            if (!show)
                continue;

            if (!first)
                sb.Append(" [grey]│[/] ");
            first = false;

            var statusEmoji = agent.GetStatusEmoji();
            sb.Append($"[{color}]{emoji} {name}[/] {statusEmoji}");
        }

        if (first) // nothing visible
            return string.Empty;

        return CenterMarkup(sb.ToString());
    }

    // ─────────────────────────────────────────────────────────────────
    // Centering helper
    // ─────────────────────────────────────────────────────────────────

    private static string CenterMarkup(string markup)
    {
        try
        {
            int consoleWidth = Console.WindowWidth > 0 ? Console.WindowWidth : 80;
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
        // Step 1: replace escaped brackets with single-char placeholders
        var temp = markup.Replace("[[", "\x01").Replace("]]", "\x02");

        // Step 2: strip markup tags
        temp = Regex.Replace(temp, @"\[[^\]]*\]", "");

        // Step 3: restore escaped brackets
        temp = temp.Replace("\x01", "[").Replace("\x02", "]");

        return temp.Length;
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
            var name = EscapeMarkup(registryAgent.Name);
            return (emoji, color, name, show);
        }

        // Fallback: use snapshot data
        var fallbackName = EscapeMarkup(ShortenMainAgentName(snapshot.DisplayName));
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

    /// <summary>
    /// Escapes Spectre markup characters in text so they don't break rendering.
    /// </summary>
    private static string EscapeMarkup(string text)
    {
        return text
            .Replace("[", "[[")
            .Replace("]", "]]");
    }
}

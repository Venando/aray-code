using System.Text;
using Spectre.Console;
using StreamShell;

namespace OpenClawPTT.Services;

/// <summary>
/// Compact left-aligned bottom panel showing agent statuses.
///
/// Toggleables (edit constants to test different layouts):
/// - <see cref="ShowAgentNames"/>: include colored agent names
/// - <see cref="UseDynamicHeight"/>: shrink to content vs fixed canvas
/// </summary>
public sealed class AgentStatusBottomPanel : IBottomPanel
{
    // ── Toggleables (change these to test different layouts) ────────────────
    private const bool ShowAgentNames = true;
    private const bool UseDynamicHeight = false;
    private const int FixedLineCountValue = 1;   // used when UseDynamicHeight == false
    private const int MaxLineCount = 1;           // upper bound for array sizing
    // ────────────────────────────────────────────────────────────────────────

    private readonly IAgentStatusTracker _tracker;
    private readonly IColorConsole _colorConsole;
    private readonly StringBuilder _sb = new(256);
    private readonly string[] _lines = new string[MaxLineCount];

    private bool _isDirty;
    private int _lastDisplayFingerprint;
    private int _cachedLineCount = 1;

    public AgentStatusBottomPanel(IAgentStatusTracker tracker, IColorConsole colorConsole)
    {
        _tracker = tracker;
        _colorConsole = colorConsole;
        _tracker.Changed += OnTrackerChanged;
        _lastDisplayFingerprint = ComputeDisplayFingerprint();
        _cachedLineCount = ComputeLineCount();
    }

    private void OnTrackerChanged()
    {
        var fingerprint = ComputeDisplayFingerprint();
        if (fingerprint != _lastDisplayFingerprint)
        {
            _lastDisplayFingerprint = fingerprint;
            _cachedLineCount = ComputeLineCount();
            _isDirty = true;
        }
    }

    public int LineCount => UseDynamicHeight ? _cachedLineCount : FixedLineCountValue;
    public bool IsDirty => _isDirty;
    public void ClearDirty() => _isDirty = false;

    public IReadOnlyList<string> GetLines(string currentInput)
    {
        if (!_isDirty)
            return _lines;

        var all = _tracker.All;
        var mainAgents = all
            .Where(s => !s.IsSubagent && AgentRegistry.Agents.Any(a => a.SessionKey == s.SessionKey))
            .ToList();
        var activeSubs = all
            .Where(s => s.IsSubagent && !ShouldHideSubagent(s))
            .ToList();

        int rowIndex = 0;
        int lineCount = ComputeLineCount();

        if (mainAgents.Count == 0 && activeSubs.Count == 0)
        {
            _lines[rowIndex++] = "[grey]No agents connected[/]";
        }
        else
        {
            _sb.Clear();
            _lines[rowIndex++] = BuildMainAgentsLine(mainAgents, _sb);

            if (false) //if (activeSubs.Count > 0)
            {
                _sb.Clear();
                _lines[rowIndex++] = BuildFlatSubagentLine(activeSubs, _sb);
            }
        }

        // Clear remaining lines in the active window
        while (rowIndex < lineCount)
            _lines[rowIndex++] = string.Empty;

        _isDirty = false;
        _cachedLineCount = lineCount;

        var lines = _lines.Where(line => line != null).ToArray();
        return lines;
    }

    private int ComputeLineCount()
    {
        if (!UseDynamicHeight)
            return FixedLineCountValue;

        var all = _tracker.All;
        bool hasMain = all.Any(s => !s.IsSubagent && AgentRegistry.Agents.Any(a => a.SessionKey == s.SessionKey));
        bool hasSubs = all.Any(s => s.IsSubagent && !ShouldHideSubagent(s));

        if (!hasMain && !hasSubs)
            return 1;
        if (hasMain && !hasSubs)
            return 1;
        return 2; // main + subagent line
    }

    // ── Line builders ───────────────────────────────────────────────────────

    private static string BuildMainAgentsLine(List<AgentStatusSnapshot> mainAgents, StringBuilder sb)
    {
        if (mainAgents.Count == 0)
            return string.Empty;

        bool first = true;
        foreach (var agent in mainAgents)
        {
            var (emoji, color, name, show) = GetAgentDisplayInfo(agent);
            if (!show)
                continue;

            if (!first)
                sb.Append(" [white bold]│[/] ");

            first = false;

            var statusEmoji = agent.GetStatusEmoji();
            sb.Append(emoji);
            sb.Append(' ');

            if (ShowAgentNames)
            {
                // Truncate long names, apply configured color
                var displayName = name.Length > 10 ? name[..10] : name;
                sb.Append('[');
                sb.Append(color);
                sb.Append(']');
                sb.Append(displayName);
                sb.Append("[/]");
                sb.Append(' ');
            }

            sb.Append(statusEmoji);
            //sb.Append(' ');
        }

        return first ? string.Empty : sb.ToString();
    }

    private static string BuildFlatSubagentLine(List<AgentStatusSnapshot> subs, StringBuilder sb)
    {
        sb.Append("[grey]└─[/] ");

        bool first = true;
        foreach (var sub in subs)
        {
            if (!first)
                sb.Append(" [grey]│[/] ");
            first = false;
            sb.Append(sub.GetStatusEmoji());
        }

        return sb.ToString();
    }

    // ── Fingerprint & helpers ────────────────────────────────────────────────

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
                hash.Add(AgentSettingsPersistenceLegacy.GetPersistedColor(registryAgent.AgentId));
                hash.Add(AgentSettingsPersistenceLegacy.GetPersistedShowInStatusPanel(registryAgent.AgentId));
            }
        }
        // Include toggles in fingerprint so changing constants forces redraw
        hash.Add(ShowAgentNames);
        hash.Add(UseDynamicHeight);
        return hash.ToHashCode();
    }

    private static bool ShouldHideSubagent(AgentStatusSnapshot s)
    {
        if (!s.IsFinished)
            return false;

        var endedOrUpdated = s.EndedAt ?? s.UpdatedAt;
        if (endedOrUpdated == null)
            return false;

        var elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - endedOrUpdated.Value;
        return elapsed > 30_000;
    }

    private static (string emoji, string color, string name, bool showInStatus) GetAgentDisplayInfo(AgentStatusSnapshot? snapshot)
    {
        if (snapshot == null)
            return ("🤖", "grey", "Agent", true);

        var registryAgent = AgentRegistry.Agents.FirstOrDefault(a => a.SessionKey == snapshot.SessionKey);
        if (registryAgent != null)
        {
            var emoji = AgentSettingsPersistenceLegacy.GetPersistedEmoji(registryAgent.AgentId) ?? "🤖";
            var color = AgentSettingsPersistenceLegacy.GetPersistedColor(registryAgent.AgentId) ?? "grey";
            var show = AgentSettingsPersistenceLegacy.GetPersistedShowInStatusPanel(registryAgent.AgentId);
            var name = Markup.Escape(registryAgent.Name);
            return (emoji, color, name, show);
        }

        var fallbackName = Markup.Escape(ShortenMainAgentName(snapshot.DisplayName));
        return ("🤖", "grey", fallbackName, true);
    }

    private static string ShortenMainAgentName(string? displayName)
    {
        if (string.IsNullOrEmpty(displayName))
            return "Agent";

        if (displayName.Contains("-main", StringComparison.OrdinalIgnoreCase))
        {
            var parts = displayName.Split('-');
            if (parts.Length >= 3)
                return parts[^2];
        }

        return displayName.Length > 16 ? displayName[..16] : displayName;
    }
}

using System.Text;
using StreamShell;

namespace OpenClawPTT.Services;

/// <summary>
/// StreamShell bottom panel that displays agent and subagent statuses.
/// Fixed at 5 lines high. Shows main agent + up to 4 subagents.
/// Status is rendered at the bottom-left corner using Spectre markup.
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

        var main = _tracker.GetMainAgent();
        var subagents = main != null
            ? _tracker.GetSubagents(main.SessionKey)
            : _tracker.All.Where(s => s.IsSubagent).ToList();

        // Line 0: Tab autocomplete suggestion slot (must be empty when no suggestion)
        lines[0] = string.Empty;

        // Line 1: Main agent status
        lines[1] = BuildMainAgentLine(main);

        // Lines 2-4: Subagent statuses (up to 3 visible)
        int idx = 2;
        foreach (var sub in subagents.Where(s => !s.IsFinished).Take(3))
        {
            if (idx < LineCountValue)
                lines[idx++] = BuildSubagentLine(sub);
        }

        // Fill remaining lines with empty
        while (idx < LineCountValue)
            lines[idx++] = string.Empty;

        return lines;
    }

    private static string BuildMainAgentLine(AgentStatusSnapshot? main)
    {
        if (main == null)
            return "[grey]No active agent[/]";

        var sb = new StringBuilder();
        sb.Append("[deepskyblue3]▸[/] ");
        sb.Append(EscapeName(main.DisplayName ?? "Agent"));
        sb.Append(' ');
        sb.Append(main.GetStatusLabel());

        if (!string.IsNullOrEmpty(main.Model))
        {
            sb.Append(" [grey](");
            sb.Append(EscapeName(main.ModelProvider ?? "?"));
            sb.Append('/');
            sb.Append(EscapeName(main.Model));
            sb.Append(")[grey]");
        }

        if (main.TotalTokens > 0)
        {
            sb.Append(" [grey]");
            sb.Append(main.TotalTokens);
            sb.Append(" tok[/]");
        }

        return sb.ToString();
    }

    private static string BuildSubagentLine(AgentStatusSnapshot sub)
    {
        var sb = new StringBuilder();
        sb.Append("  [grey]├─[/] ");
        sb.Append(EscapeName(sub.DisplayName ?? "subagent"));
        sb.Append(' ');
        sb.Append(sub.GetStatusLabel());

        if (sub.RuntimeMs > 0)
        {
            sb.Append(" [grey](");
            sb.Append((sub.RuntimeMs.Value / 1000.0).ToString("F1"));
            sb.Append("s)[/]");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Escapes Spectre markup characters in agent names so they don't break rendering.
    /// </summary>
    private static string EscapeName(string name)
    {
        return name
            .Replace("[", "[[")
            .Replace("]", "]]")
            .Replace("<", "<<")
            .Replace(">", ">>");
    }
}

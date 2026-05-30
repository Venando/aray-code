using ArayCode.Formatting;
using ArayCode.Services.Commands;
using ArayCode.Services.Themes;
using Spectre.Console;

namespace ArayCode.Services;

/// <summary>
/// Pure formatting helpers for rendering a single agent status line.
/// No mutable state — all methods are static and idempotent.
/// </summary>
public static class AgentStatusLineRenderer
{
    // ── Layout ───────────────────────────────────────────────────────────
    public const int NameColWidth = 12;  // "• " (win) / " • " (unix) + name (max 10)
    public const int TimeColWidth = 4;   // "12m", "1h", etc.
    public const int GapAfterName = 2;
    public const int GapBeforeTime = 2;
    public const int AllMargins = NameColWidth + TimeColWidth + GapAfterName + GapBeforeTime;

    public const int MaxNameDisplayLength = 10;

    // ── Rendering ───────────────────────────────────────────────────────

    public static string RenderAgentLine(
        string name,
        string bullet,
        string action,
        string timeAgo,
        bool selected,
        bool isActive)
    {
        var consoleWidth = ConsoleMetrics.GetWindowWidth();

        // Left column: "• Name" padded to NameColWidth (display-width aware)
        var tools = ThemeProvider.Current.Tools;
        var nameDisplay = selected
            ? $"[{tools.Panel.SelectedName}]{name}[/]"
            : name;
        nameDisplay = isActive
            ? $"[{tools.Panel.ActiveName}]{nameDisplay}[/]"
            : nameDisplay;
        var leftCol = OperatingSystem.IsWindows()
            ? $"{bullet} {nameDisplay}"
            : $" {bullet} {nameDisplay}";
        int bulletWidth = CharacterWidth.GetDisplayWidth(StripMarkup(bullet));
        int nameDisplayWidth = CharacterWidth.GetDisplayWidth(StripMarkup(nameDisplay));
        int leftRaw = bulletWidth + 1 + nameDisplayWidth;
        int leftPad = NameColWidth - leftRaw;
        var leftPadded = leftPad > 0 ? leftCol + new string(' ', leftPad) : leftCol;

        // Action: escape + truncate (using display width, not string length)
        int usedWidth = NameColWidth + GapAfterName + GapBeforeTime + TimeColWidth + 1;
        int actionMax = consoleWidth - usedWidth;
        var actionRaw = action;
        int actionDisplayWidth = CharacterWidth.GetDisplayWidth(actionRaw);
        if (actionDisplayWidth > actionMax && actionMax > 3)
        {
            actionRaw = TruncateByDisplayWidth(actionRaw, actionMax - 1) + "…";
            actionDisplayWidth = CharacterWidth.GetDisplayWidth(actionRaw);
        }
        else if (actionDisplayWidth > actionMax)
        {
            actionRaw = TruncateByDisplayWidth(actionRaw, actionMax);
            actionDisplayWidth = CharacterWidth.GetDisplayWidth(actionRaw);
        }
        var actionDisplay = Markup.Escape(actionRaw);
        int gapAfterAction = consoleWidth - NameColWidth - GapAfterName - actionDisplayWidth - GapBeforeTime - TimeColWidth - 1;
        if (gapAfterAction < 0) gapAfterAction = 0;

        var timePadded = timeAgo.PadLeft(TimeColWidth);

        actionDisplay = isActive
                ? $"[{tools.Panel.ActiveAgentAction}]{actionDisplay}[/]"
                : actionDisplay;

        actionDisplay = selected
                ? $"[{tools.Panel.ActionSelected}]{actionDisplay}[/]"
                : $"[{tools.Panel.Action}]{actionDisplay}[/]";

        var line = leftPadded
            + new string(' ', GapAfterName)
            + actionDisplay
            + new string(' ', gapAfterAction + GapBeforeTime)
            + $"[{tools.Panel.Time}]{timePadded}[/]";

        return selected ? $"[on {tools.Panel.SelectedBg}]{line}[/]" : line;
    }

    // ── Name helpers ────────────────────────────────────────────────────

    public static string GetAgentName(string? agentId, string sessionKey)
    {
        if (agentId is not null)
        {
            var agent = AgentRegistry.Agents.FirstOrDefault(a => a.AgentId == agentId);
            if (agent is not null)
                return FormatName(agent.Name);
        }

        return FormatName(sessionKey);
    }

    public static string FormatName(string? raw)
    {
        var name = raw ?? "?";
        var displayWidth = CharacterWidth.GetDisplayWidth(name);
        if (displayWidth > MaxNameDisplayLength)
        {
            var truncated = TruncateByDisplayWidth(name, MaxNameDisplayLength);
            return Markup.Escape(truncated);
        }
        return Markup.Escape(name);
    }

    // ── Time formatting ───────────────────────────────────────────────────

    /// <summary>Formats a Unix-ms timestamp as a relative time string.</summary>
    public static string? FormatRelativeTime(long? timestampMs)
    {
        if (timestampMs is not { } ts) return null;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var diff = now - ts;

        if (diff < 0) return "now";

        var seconds = diff / 1000;
        if (seconds < 60) return $"{seconds}s";
        var minutes = seconds / 60;
        if (minutes < 60) return $"{minutes}m";
        var hours = minutes / 60;
        if (hours < 24) return $"{hours}h";
        var days = hours / 24;
        return $"{days}d";
    }

    // ── Width helpers ────────────────────────────────────────────────────

    /// <summary>Truncates a string so its display width does not exceed <paramref name="maxWidth"/>.</summary>
    public static string TruncateByDisplayWidth(string text, int maxWidth)
    {
        if (maxWidth <= 0) return string.Empty;

        var sb = new System.Text.StringBuilder();
        int width = 0;
        foreach (char c in text)
        {
            int charWidth = CharacterWidth.GetDisplayWidth(c.ToString());
            if (width + charWidth > maxWidth)
                break;
            sb.Append(c);
            width += charWidth;
        }
        return sb.ToString();
    }

    public static string StripMarkup(string markup)
    {
        if (string.IsNullOrEmpty(markup)) return string.Empty;
        var sb = new System.Text.StringBuilder(markup.Length);
        bool inTag = false;
        for (int i = 0; i < markup.Length; i++)
        {
            char c = markup[i];
            if (c == '[' && i + 1 < markup.Length)
            {
                if (markup[i + 1] == '[') { sb.Append('['); i++; continue; }
                inTag = true;
                continue;
            }
            if (c == ']' && inTag) { inTag = false; continue; }
            if (!inTag) sb.Append(c);
        }
        return sb.ToString();
    }
}

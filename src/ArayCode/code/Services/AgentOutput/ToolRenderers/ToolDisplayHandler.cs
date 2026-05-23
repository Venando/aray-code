using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ArayCode.Services.Themes;
using Spectre.Console;

namespace ArayCode.Services;

public sealed class ToolDisplayHandler
{
    private readonly int _rightMarginIndent;
    private readonly Dictionary<string, IToolRenderer> _renderers;
    private readonly IToolOutput _output;
    private readonly IStreamShellHost? _shellHost;

    private static readonly Dictionary<string, string> ToolIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["read"]           = "📄",
        ["write"]          = "📝",
        ["edit"]           = "🪄",
        ["exec"]           = "▶️",
        ["process"]        = "⚙️",
        ["web_search"]     = "🔍",
        ["web_fetch"]      = "🌐",
        ["sessions_list"]  = "📋",
        ["session_status"] = "📋",
        ["memory_search"]  = "📚",
        ["memory_get"]     = "📚",
        ["image_generate"] = "🎨",
        ["subagents"]      = "🎮🤖",
        ["sessions_spawn"] = "➕🤖",
        ["update_plan"]  = "📋",
        ["plan"]          = "📋",
    };

    public ToolDisplayHandler(IToolOutput output, IEnumerable<IToolRenderer> renderers, int rightMarginIndent, IStreamShellHost? shellHost = null)
    {
        _output = output;
        _rightMarginIndent = rightMarginIndent;
        _shellHost = shellHost;
        _renderers = renderers
            .Where(r => !string.IsNullOrEmpty(r.ToolName))
            .ToDictionary(r => r.ToolName, r => r, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Convenience constructor that creates a ToolOutputHelper and default renderers
    /// all connected to the same output, so renderer output is captured for Flush().
    /// </summary>
    public ToolDisplayHandler(int rightMarginIndent, IStreamShellHost? shellHost = null)
    {
        _rightMarginIndent = rightMarginIndent;
        _shellHost = shellHost;
        _output = new ToolOutputHelper(shellHost!, _rightMarginIndent);
        _renderers = BuildDefaultRenderers(_output)
            .Where(r => !string.IsNullOrEmpty(r.ToolName))
            .ToDictionary(r => r.ToolName, r => r, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<IToolRenderer> BuildDefaultRenderers(IToolOutput output)
    {
        yield return new ReadToolRenderer(output);
        yield return new WriteToolRenderer(output);
        yield return new EditToolRenderer(output);
        yield return new ExecToolRenderer(output);
        yield return new WebFetchToolRenderer(output);
        yield return new SessionsListToolRenderer(output);
        yield return new SessionStatusToolRenderer(output);
        yield return new MemorySearchToolRenderer(output);
        yield return new MemoryGetToolRenderer(output);
        yield return new SubagentsToolRenderer(output);
        yield return new SessionsSpawnToolRenderer(output);
        yield return new UpdatePlanToolRenderer(output);
    }

    public void Handle(string toolName, string arguments)
    {
        if (string.IsNullOrEmpty(toolName))
            return;

        string icon = ToolIcons.TryGetValue(toolName, out var i) ? i : "🔧";
        string displayName = string.Join(" ", toolName.Split('_').Select(w => char.ToUpper(w[0]) + w[1..]));
        var toolsTheme = ThemeProvider.Current.Tools;
        string headerLine = $"[{toolsTheme.HeaderStyle}]  {icon} {displayName}[/]  ";


        if (string.IsNullOrWhiteSpace(arguments))
        {
            _shellHost?.AddMessage(headerLine);
            _shellHost?.AddMessage("");
            return;
        }

        _output.Start(headerLine);

        try
        {
            using var doc = JsonDocument.Parse(arguments);
            if (_renderers.TryGetValue(toolName, out var renderer))
            {
                renderer.Render(doc.RootElement, _rightMarginIndent);
            }
            else
            {
                // Fall back to generic KVP for unregistered tools
                var generic = new GenericKvpToolRenderer(_output);
                generic.Render(doc.RootElement, _rightMarginIndent);
            }
        }
        catch
        {
            var tools = ThemeProvider.Current.Tools;
            _shellHost?.AddMessage($"[{tools.General.Muted}]  {Markup.Escape(arguments)}[/]");
        }

        _output.PrintLine("");

        _output.Finish();
        _output.Flush();
    }
}

using OpenClawPTT.Services.Diagnostics;
using OpenClawPTT.Services.Themes;
using Spectre.Console;

namespace OpenClawPTT.Services.Commands;

/// <summary>Native command: /errors — displays recent gateway error log entries.</summary>
public sealed class ErrorsCommand : ICommand
{
    private readonly IStreamShellHost _host;
    private readonly ErrorLogStore _errorLog;

    public string Name => "errors";
    public string Description => "[N] Show recent gateway errors";
    public CommandSource Source => CommandSource.Native;
    public ShellCommandType Type => ShellCommandType.Diagnostics;
    public string[]? Suggestions => null;

    public ErrorsCommand(IStreamShellHost host, ErrorLogStore errorLog)
    {
        _host = host;
        _errorLog = errorLog;
    }

    public Task ExecuteAsync(string[] args, Dictionary<string, string> namedArgs, CancellationToken ct = default)
    {
        if (args.Length > 0 && args[0].Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            _errorLog.Clear();
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Success}]  Error log cleared.[/]");
            return Task.CompletedTask;
        }

        int count = 10;
        if (args.Length > 0 && int.TryParse(args[0], out var requested))
            count = Math.Clamp(requested, 1, 100);

        var entries = _errorLog.GetRecent(count);

        if (entries.Count == 0)
        {
            _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Success}]  No errors logged.[/]");
            return Task.CompletedTask;
        }

        _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Highlight}]  Recent errors ({entries.Count}):[/]");
        foreach (var entry in entries)
        {
            var ts = entry.Timestamp.ToString("HH:mm:ss");

            var codeStr = entry.Code;
            if (!string.IsNullOrEmpty(entry.OuterCode) && entry.OuterCode != entry.Code)
                codeStr = $"{entry.OuterCode} → {entry.Code}";
            _host.AddMessage($"  [{ThemeProvider.Current.Tools.General.Muted}]{ts}[/] [{ThemeProvider.Current.Tools.Messages.Emphasis}]{Markup.Escape(codeStr)}[/] {Markup.Escape(entry.Message)}");

            if (!string.IsNullOrEmpty(entry.Reason))
                _host.AddMessage($"    Reason: [{ThemeProvider.Current.Tools.General.Muted}]{Markup.Escape(entry.Reason)}[/]");
            if (!string.IsNullOrEmpty(entry.RequestId))
                _host.AddMessage($"    RequestId: [{ThemeProvider.Current.Tools.General.Muted}]{Markup.Escape(entry.RequestId)}[/]");
            if (!string.IsNullOrEmpty(entry.DeviceId))
                _host.AddMessage($"    DeviceId: [{ThemeProvider.Current.Tools.General.Muted}]{Markup.Escape(entry.DeviceId)}[/]");
            if (!string.IsNullOrEmpty(entry.RequestedRole))
                _host.AddMessage($"    Requested role: [{ThemeProvider.Current.Tools.General.Muted}]{Markup.Escape(entry.RequestedRole)}[/]");
            if (entry.RequestedScopes is { Length: > 0 })
                _host.AddMessage($"    Requested scopes: [{ThemeProvider.Current.Tools.General.Muted}]{Markup.Escape(string.Join(", ", entry.RequestedScopes))}[/]");
            if (entry.ApprovedScopes is { Length: > 0 })
                _host.AddMessage($"    Approved scopes: [{ThemeProvider.Current.Tools.General.Muted}]{Markup.Escape(string.Join(", ", entry.ApprovedScopes))}[/]");
            if (entry.ApprovedRoles is { Length: > 0 })
                _host.AddMessage($"    Approved roles: [{ThemeProvider.Current.Tools.General.Muted}]{Markup.Escape(string.Join(", ", entry.ApprovedRoles))}[/]");
            if (!string.IsNullOrEmpty(entry.Method))
                _host.AddMessage($"    Method: [{ThemeProvider.Current.Tools.General.Muted}]{Markup.Escape(entry.Method)}[/]");
            if (entry.RetryAfterMs.HasValue)
                _host.AddMessage($"    Retry after: [{ThemeProvider.Current.Tools.General.Muted}]{entry.RetryAfterMs.Value}ms[/]");
            if (!string.IsNullOrEmpty(entry.RecommendedNextStep))
                _host.AddMessage($"    Recommended: [{ThemeProvider.Current.Tools.General.Muted}]{Markup.Escape(entry.RecommendedNextStep)}[/]");
            if (entry.CanRetryWithDeviceToken == true)
                _host.AddMessage($"    Can retry with device token: [{ThemeProvider.Current.Tools.General.Muted}]yes[/]");

            if (entry.SuggestedActions.Length > 0)
            {
                foreach (var action in entry.SuggestedActions)
                    _host.AddMessage($"    → [{ThemeProvider.Current.Tools.General.Muted}]{Markup.Escape(action)}[/]");
            }
        }
        _host.AddMessage($"[{ThemeProvider.Current.Tools.Messages.Info}]  Use /errors N to show more, /errors clear to clear[/]");
        return Task.CompletedTask;
    }
}

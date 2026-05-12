using OpenClawPTT.Services.Diagnostics;
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
    public CommandType Type => CommandType.Diagnostics;
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
            _host.AddMessage("[green]  Error log cleared.[/]");
            return Task.CompletedTask;
        }

        int count = 10;
        if (args.Length > 0 && int.TryParse(args[0], out var requested))
            count = Math.Clamp(requested, 1, 100);

        var entries = _errorLog.GetRecent(count);

        if (entries.Count == 0)
        {
            _host.AddMessage("[green]  No errors logged.[/]");
            return Task.CompletedTask;
        }

        _host.AddMessage($"[cyan2]  Recent errors ({entries.Count}):[/]");
        foreach (var entry in entries)
        {
            var ts = entry.Timestamp.ToString("HH:mm:ss");

            var codeStr = entry.Code;
            if (!string.IsNullOrEmpty(entry.OuterCode) && entry.OuterCode != entry.Code)
                codeStr = $"{entry.OuterCode} → {entry.Code}";
            _host.AddMessage($"  [grey]{ts}[/] [bold]{Markup.Escape(codeStr)}[/] {Markup.Escape(entry.Message)}");

            if (!string.IsNullOrEmpty(entry.Reason))
                _host.AddMessage($"    Reason: [grey]{Markup.Escape(entry.Reason)}[/]");
            if (!string.IsNullOrEmpty(entry.RequestId))
                _host.AddMessage($"    RequestId: [grey]{Markup.Escape(entry.RequestId)}[/]");
            if (!string.IsNullOrEmpty(entry.DeviceId))
                _host.AddMessage($"    DeviceId: [grey]{Markup.Escape(entry.DeviceId)}[/]");
            if (!string.IsNullOrEmpty(entry.RequestedRole))
                _host.AddMessage($"    Requested role: [grey]{Markup.Escape(entry.RequestedRole)}[/]");
            if (entry.RequestedScopes is { Length: > 0 })
                _host.AddMessage($"    Requested scopes: [grey]{Markup.Escape(string.Join(", ", entry.RequestedScopes))}[/]");
            if (entry.ApprovedScopes is { Length: > 0 })
                _host.AddMessage($"    Approved scopes: [grey]{Markup.Escape(string.Join(", ", entry.ApprovedScopes))}[/]");
            if (entry.ApprovedRoles is { Length: > 0 })
                _host.AddMessage($"    Approved roles: [grey]{Markup.Escape(string.Join(", ", entry.ApprovedRoles))}[/]");
            if (!string.IsNullOrEmpty(entry.Method))
                _host.AddMessage($"    Method: [grey]{Markup.Escape(entry.Method)}[/]");
            if (entry.RetryAfterMs.HasValue)
                _host.AddMessage($"    Retry after: [grey]{entry.RetryAfterMs.Value}ms[/]");
            if (!string.IsNullOrEmpty(entry.RecommendedNextStep))
                _host.AddMessage($"    Recommended: [grey]{Markup.Escape(entry.RecommendedNextStep)}[/]");
            if (entry.CanRetryWithDeviceToken == true)
                _host.AddMessage($"    Can retry with device token: [grey]yes[/]");

            if (entry.SuggestedActions.Length > 0)
            {
                foreach (var action in entry.SuggestedActions)
                    _host.AddMessage($"    → [grey]{Markup.Escape(action)}[/]");
            }
        }
        _host.AddMessage("[grey]  Use /errors N to show more, /errors clear to clear[/]");
        return Task.CompletedTask;
    }
}

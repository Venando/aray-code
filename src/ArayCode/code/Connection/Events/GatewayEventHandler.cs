using System.Text.Json;
using ArayCode.Services;

namespace ArayCode;

/// <summary>
/// Handles raw gateway events that are relevant for user display.
/// Shows error notifications, usage warnings, and other noteworthy gateway events.
/// </summary>
public class GatewayEventHandler : IEventHandler<GatewayEvent>
{
    private readonly IColorConsole _console;

    private static readonly HashSet<string> NoteworthyEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "error",
        "warning",
        "model.failover",
    };

    private static readonly HashSet<string> QuietEvents = new(StringComparer.OrdinalIgnoreCase)
    {
        "heartbeat",
        "presence",
        "tick",
        "sessions.changed",
        "device.pair.requested",
        "device.pair.resolved",
        "node.pair.requested",
        "node.pair.resolved",
        "plugin.approval.requested",
        "plugin.approval.resolved",
    };

    public GatewayEventHandler(IColorConsole console)
    {
        _console = console;
    }

    public Task HandleAsync(GatewayEvent evt)
    {
        // Skip quiet transport events
        if (QuietEvents.Contains(evt.Name))
            return Task.CompletedTask;

        // Show noteworthy events
        if (NoteworthyEvents.Contains(evt.Name))
        {
            var message = ExtractMessage(evt.Payload);
            _console.PrintWarning($"Gateway: {evt.Name} — {message}");
            return Task.CompletedTask;
        }

        // Log unknown events at debug level only
        _console.Log("gateway", $"Event: {evt.Name}", LogLevel.Debug);

        return Task.CompletedTask;
    }

    private static string ExtractMessage(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return string.Empty;

        if (payload.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
            return msg.GetString() ?? string.Empty;

        if (payload.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            return text.GetString() ?? string.Empty;

        if (payload.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String)
            return err.GetString() ?? string.Empty;

        if (payload.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String)
            return reason.GetString() ?? string.Empty;

        return string.Empty;
    }
}

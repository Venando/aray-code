using System;
using ArayCode.Services;

namespace ArayCode;

/// <summary>
/// Handles model fallback events by displaying a colored warning in the console.
/// Shows the failed provider/model and which fallback was selected.
/// </summary>
public class ModelFallbackHandler : IEventHandler<ModelFallbackEvent>
{
    private readonly IColorConsole _console;

    /// <summary>
    /// Initializes the handler with a required console instance.
    /// </summary>
    /// <param name="console">The colored console for displaying fallback notifications.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="console"/> is null.</exception>
    public ModelFallbackHandler(IColorConsole console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    public Task HandleAsync(ModelFallbackEvent evt)
    {
        // model.failover events are only dispatched when a fallback occurs,
        // so we always display the notification.
        _console.PrintModelFallback(
            evt.FailedProvider ?? "Unknown",
            evt.FailedModel ?? "Unknown",
            evt.FallbackProvider ?? "Unknown",
            evt.FallbackModel ?? "Unknown",
            isQuotaError: false); // quota detection happens in SessionMessageHandler

        return Task.CompletedTask;
    }
}

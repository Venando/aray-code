using System.Text.Json;
using OpenClawPTT.Services;

namespace OpenClawPTT;

/// <summary>
/// Handles model fallback events by displaying a colored warning in the console.
/// Shows the failed provider/model and which fallback was selected.
/// </summary>
public class ModelFallbackHandler : IEventHandler<ModelFallbackEvent>
{
    private readonly IColorConsole _console;

    public ModelFallbackHandler(IColorConsole? console = null)
    {
        _console = console ?? new ColorConsole(new StreamShellHost());
    }

    public Task HandleAsync(ModelFallbackEvent evt)
    {
        // Only show once for the final decision (succeeded or all failed)
        if (evt.Decision == "candidate_failed")
        {
            // Intermediate failures are handled internally; 
            // only show the final outcome
            return Task.CompletedTask;
        }

        if (evt.Succeeded)
        {
            _console.PrintModelFallback(
                evt.FailedProvider ?? "Unknown",
                evt.FailedModel ?? "Unknown",
                evt.FallbackProvider ?? "Unknown",
                evt.FallbackModel ?? "Unknown",
                evt.IsQuotaError);
        }
        else
        {
            _console.PrintModelFailed(evt.ErrorMessage ?? "Unknown error");
        }

        return Task.CompletedTask;
    }
}

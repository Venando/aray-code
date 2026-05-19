using System.Text.Json;

namespace OpenClawPTT.Services;

internal sealed class UpdatePlanActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "update_plan";

    public string Render(JsonElement args)
    {
        if (args.TryGetProperty("plan", out var planProp) && planProp.ValueKind == JsonValueKind.Array)
        {
            int count = 0;
            int completed = 0;
            foreach (var step in planProp.EnumerateArray())
            {
                count++;
                if (step.TryGetProperty("status", out var statusProp) &&
                    statusProp.GetString()?.ToLowerInvariant() == "completed")
                    completed++;
            }
            return $"Updating plan ({completed}/{count} completed)";
        }

        return "Updating plan";
    }
}

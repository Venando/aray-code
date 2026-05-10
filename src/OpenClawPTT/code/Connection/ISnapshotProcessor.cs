using System.Text.Json;

namespace OpenClawPTT;

/// <summary>
/// Processes snapshot data from the gateway hello payload.
/// </summary>
public interface ISnapshotProcessor
{
    /// <summary>
    /// Processes the snapshot contained in the hello payload.
    /// Extracts agent information and updates the AgentRegistry and optional AgentStatusTracker.
    /// </summary>
    /// <param name="hello">The hello payload JsonElement.</param>
    void ProcessSnapshot(JsonElement hello);
}

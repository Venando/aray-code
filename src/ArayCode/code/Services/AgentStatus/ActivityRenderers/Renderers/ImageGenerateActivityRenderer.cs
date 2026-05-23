using System.Text.Json;

namespace ArayCode.Services;

internal sealed class ImageGenerateActivityRenderer : IAgentActivityRenderer
{
    public string ToolName => "image_generate";

    public string Render(JsonElement args)
    {
        var prompt = AgentActivityRendererHelpers.GetString(args, "prompt");
        if (prompt is null) return "Generating image";

        return $"Generating image: {AgentActivityRendererHelpers.Truncate(prompt, 50)}";
    }
}

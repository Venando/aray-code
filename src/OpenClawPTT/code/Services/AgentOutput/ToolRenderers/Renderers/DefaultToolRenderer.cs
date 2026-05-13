using System.Text.Json;

namespace OpenClawPTT.Services;

/// <summary>
/// Default renderer for unknown tools that were not registered with a specific renderer.
/// Falls back to printing the raw arguments. NOTE: this renderer returns an empty
/// ToolName so it is filtered out of the default registry.
/// </summary>
public sealed class DefaultToolRenderer : ToolRendererBase
{
    public DefaultToolRenderer(IToolOutput output) : base(output)
    {
    }

    public override string ToolName => "";

    public override void Render(JsonElement args, int rightMarginIndent)
    {
        RenderKvpProperties(Output, args);
    }
}

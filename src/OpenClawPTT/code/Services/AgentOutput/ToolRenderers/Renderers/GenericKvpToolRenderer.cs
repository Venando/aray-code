using System.Text.Json;

namespace OpenClawPTT.Services;

/// <summary>
/// Renders tools that use simple key-value pair display (process, web_search, image_generate).
/// </summary>
public sealed class GenericKvpToolRenderer : ToolRendererBase
{
    public GenericKvpToolRenderer(IToolOutput output) : base(output)
    {
    }

    public override string ToolName => "generic_kvp";

    public override void Render(JsonElement args, int rightMarginIndent)
    {
        RenderKvpProperties(Output, args);
    }
}

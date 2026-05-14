using System.Text.Json;

namespace OpenClawPTT.Services;

public sealed class EditToolRenderer : ToolRendererBase
{
    private readonly DiffRenderer _diffRenderer;

    public EditToolRenderer(IToolOutput output) : base(output)
    {
        _diffRenderer = new DiffRenderer(output);
    }

    public override string ToolName => "edit";

    public override void Render(JsonElement args, int rightMarginIndent)
    {
        if (args.TryGetProperty("path", out var pathProp))
        {
            string displayPath = FilePathDisplayHelper.FormatDisplayPath(pathProp.GetString() ?? "");
            Output.Print(displayPath, Style.General.Label);
        }

        if (args.TryGetProperty("edits", out var editsProp) && editsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var edit in editsProp.EnumerateArray())
            {
                string oldText = edit.TryGetProperty("oldText", out var oldProp) ? oldProp.GetString() ?? "" : "";
                string newText = edit.TryGetProperty("newText", out var newProp) ? newProp.GetString() ?? "" : "";

                Output.PrintLine("");

                var diffResult = LineDiffEngine.ComputeDiff(oldText, newText);

                if (diffResult.Entries.Count == 0)
                {
                    const string prefix = "  ";
                    _diffRenderer.RenderPlainText(oldText, prefix, rightMarginIndent, maxRows: 8);
                }
                else
                {
                    _diffRenderer.RenderDiff(diffResult, maxRows: 8);
                }
            }
        }
    }
}

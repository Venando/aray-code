using System.Text.Json;
using Spectre.Console;

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
        // Schema: { "path": "...", "edits": [{ "oldText": "...", "newText": "..." }] }
        if (args.TryGetProperty("path", out var pathProp))
        {
            string displayPath = FilePathDisplayHelper.FormatDisplayPath(pathProp.GetString() ?? "");
            Output.Print(displayPath, ConsoleColor.Gray);
        }

        if (args.TryGetProperty("edits", out var editsProp) && editsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var edit in editsProp.EnumerateArray())
            {
                string oldText = edit.TryGetProperty("oldText", out var oldProp) ? oldProp.GetString() ?? "" : "";
                string newText = edit.TryGetProperty("newText", out var newProp) ? newProp.GetString() ?? "" : "";

                Output.PrintLine("");

                // Compute and render diff between oldText and newText
                var diffResult = LineDiffEngine.ComputeDiff(oldText, newText);

                if (diffResult.Entries.Count == 0)
                {
                    // No differences — just show the content once
                    const string prefix = "  ";
                    _diffRenderer.RenderPlainText(oldText, prefix, rightMarginIndent, maxRows: 8);
                }
                else
                {
                    // Delegate to DiffRenderer for compact diff rendering (max 8 lines)
                    _diffRenderer.RenderDiff(diffResult, maxRows: 8);
                }
            }
        }
    }


}

using System.Text.Json;
using ArayCode.Services.Themes;

namespace ArayCode.Services;

/// <summary>
/// Base class for tool renderers providing common helper methods.
/// All color/style values are read from <see cref="ThemeProvider.Current.Tools"/>
/// so themes can override the appearance of any rendered element.
/// </summary>
public abstract class ToolRendererBase : IToolRenderer
{
    protected readonly IToolOutput Output;
    protected static ToolTheme Style => ThemeProvider.Current.Tools;

    protected ToolRendererBase(IToolOutput output)
    {
        Output = output ?? throw new ArgumentNullException(nameof(output));
    }

    public abstract string ToolName { get; }
    public abstract void Render(JsonElement args, int rightMarginIndent);

    /// <summary>
    /// Prints a label followed by a value.
    /// </summary>
    protected void PrintLabelValue(string label, string? value, bool prependComma = false)
    {
        if (prependComma)
        {
            Output.Print(", ", Style.General.Separator);
        }
        Output.Print(label, Style.Kvp.Label);
        Output.Print(value ?? "", Style.Kvp.Value);
    }

    /// <summary>
    /// Prints a value with the specified style.
    /// </summary>
    protected void PrintValue(string? value, string? style = null)
    {
        Output.Print(value ?? "", style ?? Style.General.Value);
    }

    /// <summary>
    /// Prints a string property if it exists in the JSON element.
    /// Returns true if the property was printed.
    /// </summary>
    protected bool PrintPropertyIfExists(JsonElement args, string propertyName, string label, bool prependComma = false)
    {
        if (!args.TryGetProperty(propertyName, out var prop))
            return false;

        var value = prop.GetString();
        if (string.IsNullOrEmpty(value))
            return false;

        PrintLabelValue(label, value, prependComma);
        return true;
    }

    /// <summary>
    /// Prints an integer property if it exists in the JSON element.
    /// Returns true if the property was printed.
    /// </summary>
    protected bool PrintIntPropertyIfExists(JsonElement args, string propertyName, string label, bool prependComma = false)
    {
        if (!args.TryGetProperty(propertyName, out var prop))
            return false;

        int value;
        try
        {
            value = prop.GetInt32();
        }
        catch
        {
            return false;
        }

        PrintLabelValue(label, value.ToString(), prependComma);
        return true;
    }

    /// <summary>
    /// Renders JSON object properties as comma-separated key-value pairs.
    /// First property is printed in gray; subsequent ones as ", key: value" in darkgray/white.
    /// </summary>
    protected static void RenderKvpProperties(IToolOutput output, JsonElement args)
    {
        var s = Style;
        bool first = true;
        foreach (var prop in args.EnumerateObject())
        {
            if (first)
            {
                output.Print(GetValueString(prop.Value), s.General.Value);
                first = false;
            }
            else
            {
                output.Print($", {prop.Name}: ", s.Kvp.Key);
                output.Print(GetValueString(prop.Value), s.Kvp.Value);
            }
        }

        if (first)
        {
            output.PrintLine("\u00b7", s.General.MutedSeparator);
        }
    }

    /// <summary>
    /// Converts a <see cref="JsonElement"/> to its string representation, handling all value types.
    /// </summary>
    public static string GetValueString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            JsonValueKind.Array => element.GetRawText(),
            JsonValueKind.Object => element.GetRawText(),
            _ => element.GetRawText()
        };
    }
}

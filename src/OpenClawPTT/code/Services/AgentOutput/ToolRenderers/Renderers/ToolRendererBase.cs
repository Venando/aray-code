using System.Text.Json;

namespace OpenClawPTT.Services;

/// <summary>
/// Base class for tool renderers providing common helper methods.
/// </summary>
public abstract class ToolRendererBase : IToolRenderer
{
    protected readonly IToolOutput Output;

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
            Output.Print(", ", ConsoleColor.Gray);
        }
        Output.Print(label, ConsoleColor.DarkGray);
        Output.Print(value ?? "", ConsoleColor.White);
    }

    /// <summary>
    /// Prints a value with the specified color.
    /// </summary>
    protected void PrintValue(string? value, ConsoleColor color = ConsoleColor.Gray)
    {
        Output.Print(value ?? "", color);
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
        bool first = true;
        foreach (var prop in args.EnumerateObject())
        {
            if (first)
            {
                output.Print(GetValueString(prop.Value), ConsoleColor.Gray);
                first = false;
            }
            else
            {
                output.Print($", {prop.Name}: ", ConsoleColor.DarkGray);
                output.Print(GetValueString(prop.Value), ConsoleColor.White);
            }
        }

        if (first)
        {
            output.PrintLine("\u00b7", ConsoleColor.Black);
        }
    }

    /// <summary>
    /// Converts a <see cref="JsonElement"/> to its string representation, handling all value types.
    /// </summary>
    protected static string GetValueString(JsonElement element)
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

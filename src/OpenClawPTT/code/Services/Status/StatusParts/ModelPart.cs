using System;

namespace OpenClawPTT.Services.StatusParts;

/// <summary>
/// Renders the active agent's model name, e.g. "kimi-k2.6".
/// Long provider-prefixed names are shortened for compact display.
/// </summary>
public sealed class ModelPart : StringStatusPartBase
{
    public ModelPart(DisplayPosition defaultPosition = DisplayPosition.TopSeparatorLeft, int order = 10)
        : base(defaultPosition, order, " ")
    {
    }

    protected override void BuildText()
    {
        var model = Value;
        if (string.IsNullOrEmpty(model))
            return;

        Builder.Append(ShortenModelName(model));
    }

    /// <summary>
    /// Shortens model names by removing the common provider prefix for compact display.
    /// E.g. "deepseek/deepseek-v4-flash" → "deepseek-v4-flash"
    ///       "kimi/kimi-k2.6" → "kimi-k2.6"
    /// </summary>
    internal static string ShortenModelName(string model)
    {
        if (string.IsNullOrEmpty(model))
            return model;

        int slashIndex = model.IndexOf('/');
        if (slashIndex > 0 && slashIndex < model.Length - 1)
        {
            var prefix = model[..slashIndex];
            var name = model[(slashIndex + 1)..];
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return name;
            if (model.Length <= 30)
                return model;
            return name;
        }
        return model;
    }
}

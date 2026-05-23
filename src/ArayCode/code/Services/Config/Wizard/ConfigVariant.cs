using StreamShell;

namespace ArayCode.ConfigWizard;

/// <summary>Simple IVariant implementation for PromptSelection.</summary>
public sealed class ConfigVariant : IVariant
{
    public string Name { get; }
    public string Value { get; }

    public ConfigVariant(string name, string value)
    {
        Name = name;
        Value = value;
    }

    public ConfigVariant(string name) : this(name, name) { }
}

public sealed class ConfigDecoration : IDecoration
{
    public string Name { get; }

    public ConfigDecoration(string name)
    {
        Name = name;
    }
}

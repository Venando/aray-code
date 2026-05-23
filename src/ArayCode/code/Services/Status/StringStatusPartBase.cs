using System;

namespace ArayCode.Services;

/// <summary>
/// Base class for status parts that track a single nullable string value.
/// Provides standard dirty-flag-on-change semantics and a default
/// <see cref="StatusPartBase.BuildText"/> that appends the value directly.
///
/// Subclasses override <see cref="StatusPartBase.BuildText"/> when the
/// rendered output needs formatting beyond the raw value
/// (e.g. shortening, wrapping in markup).
/// </summary>
public abstract class StringStatusPartBase : StatusPartBase
{
    private string? _value;

    protected StringStatusPartBase(DisplayPosition defaultPosition, int order, string separatorBefore)
        : base(defaultPosition, order)
    {
        SeparatorBefore = separatorBefore;
    }

    /// <inheritdoc />
    public override string SeparatorBefore { get; }

    /// <summary>
    /// Returns the raw tracked value for use by subclasses in <see cref="StatusPartBase.BuildText"/>.
    /// </summary>
    protected string? Value => _value;

    /// <summary>
    /// Feeds a new value. Marks dirty only when the value actually changes.
    /// </summary>
    public void Update(string? value)
    {
        if (!string.Equals(_value, value, StringComparison.Ordinal))
        {
            _value = value;
            MarkDirty();
        }
    }

    /// <summary>
    /// Clears the tracked value alongside marking dirty.
    /// </summary>
    protected void Clear()
    {
        if (_value is not null)
        {
            _value = null;
            MarkDirty();
        }
    }

    protected override void BuildText()
    {
        if (!string.IsNullOrEmpty(_value))
            Builder.Append(_value);
    }
}

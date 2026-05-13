using System.Text;

namespace OpenClawPTT.Services;

/// <summary>
/// Base class for status info parts providing dirty-flag tracking,
/// Spectre-markup text caching, and StringBuilder reuse.
///
/// Thread-safety: callers must synchronize calls to
/// <see cref="MarkDirty"/>/<see cref="MarkClean"/> externally
/// (typically via the lock in <see cref="StatusService"/>).
/// </summary>
public abstract class StatusPartBase : IStatusPart
{
    private string? _cachedText;
    private bool _dirty = true;
    private DisplayPosition _position;

    /// <summary>
    /// When true, <see cref="GetText()"/> always rebuilds the rendered text
    /// instead of using the cached value (used for animations).
    /// </summary>
    protected bool AlwaysRebuild { get; set; }

    /// <summary>Reusable StringBuilder for rendering subclasses.</summary>
    protected readonly StringBuilder Builder = new(128);

    protected StatusPartBase(DisplayPosition defaultPosition, int order)
    {
        _position = defaultPosition;
        Order = order;
    }

    /// <inheritdoc />
    public string GetText()
    {
        if (!AlwaysRebuild && !_dirty && _cachedText is not null)
            return _cachedText;

        Builder.Clear();
        BuildText();
        _cachedText = Builder.ToString();
        if (AlwaysRebuild)
            _dirty = true;
        // _dirty stays true until MarkClean() is called by the owner
        return _cachedText;
    }

    /// <inheritdoc />
    public bool IsDirty
    {
        get => _dirty;
        private set => _dirty = value;
    }

    /// <inheritdoc />
    public void MarkClean()
    {
        _dirty = false;
    }

    /// <summary>Marks this part dirty so the next <see cref="GetText()"/> call rebuilds the cache.</summary>
    protected void MarkDirty()
    {
        _dirty = true;
    }

    /// <inheritdoc />
    public DisplayPosition Position
    {
        get => _position;
        set
        {
            if (_position != value)
            {
                _position = value;
                MarkDirty();
            }
        }
    }

    /// <inheritdoc />
    public int Order { get; set; }

    /// <summary>
    /// Converts a <see cref="StatusColor"/> to its Spectre.Console markup color name.
    /// </summary>
    protected static string ToMarkupColor(StatusColor color) => color switch
    {
        StatusColor.Green => "green",
        StatusColor.Yellow => "yellow",
        StatusColor.Red => "red",
        _ => "yellow",
    };

    /// <inheritdoc />
    public abstract string SeparatorBefore { get; }

    /// <summary>
    /// Subclasses implement this to append their Spectre-markup text
    /// to <see cref="Builder"/>. The base class handles caching.
    /// </summary>
    protected abstract void BuildText();
}

namespace OpenClawPTT.Services;

/// <summary>
/// Defines where a status info part can be displayed.
/// Each status part (active agent name, model, thinking level, etc.)
/// can be independently positioned by configuring its
/// <see cref="DisplayPosition"/> in <see cref="AppConfig"/>.
/// </summary>
public enum DisplayPosition
{
    /// <summary>Do not display this status part at all.</summary>
    None,

    /// <summary>Display on the left side of the StreamShell top separator.</summary>
    TopSeparatorLeft,

    /// <summary>Display on the right side of the StreamShell top separator.</summary>
    TopSeparatorRight,

    /// <summary>Display on the left side of the StreamShell bottom separator.</summary>
    BottomSeparatorLeft,

    /// <summary>Display on the right side of the StreamShell bottom separator.</summary>
    BottomSeparatorRight,

    /// <summary>Display on the left side of the app status bottom panel.</summary>
    AppStatusPanelLeft,

    /// <summary>Display on the right side of the app status bottom panel.</summary>
    AppStatusPanelRight,
}

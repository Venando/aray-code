namespace ArayCode.Services;

/// <summary>
/// Named colors used to convey component status on the StreamShell separator bar.
/// Maps to Spectre.Console markup color names internally.
/// </summary>
public enum StatusColor
{
    /// <summary>Component is connected / operational.</summary>
    Green,

    /// <summary>Component is starting / connecting / in transition.</summary>
    Yellow,

    /// <summary>Component is disconnected / in error state.</summary>
    Red,
}

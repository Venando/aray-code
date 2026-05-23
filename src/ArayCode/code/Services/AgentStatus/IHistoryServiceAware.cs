using ArayCode.Services.Commands;

namespace ArayCode.Services;

/// <summary>
/// Interface for bottom panels that need a <see cref="SessionHistoryService"/>
/// reference to function. Used by <see cref="StreamShellHost.ResetDefaultPanel"/>
/// to re-inject the service into panels recreated via the registered factory.
/// </summary>
internal interface IHistoryServiceAware
{
    void SetHistoryService(SessionHistoryService service);
}

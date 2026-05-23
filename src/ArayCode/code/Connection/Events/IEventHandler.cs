namespace ArayCode;

/// <summary>
/// Handles a single typed event.
/// </summary>
public interface IEventHandler<TEvent>
{
    /// <summary>Handle the event asynchronously.</summary>
    Task HandleAsync(TEvent evt);
}

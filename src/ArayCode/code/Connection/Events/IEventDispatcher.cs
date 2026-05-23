namespace ArayCode;

/// <summary>
/// Dispatches typed events to registered handlers.
/// Provides synchronous registration and both async and fire-and-forget dispatch.
/// </summary>
public interface IEventDispatcher
{
    /// <summary>Register a handler for a specific event type.</summary>
    void RegisterHandler<TEvent>(IEventHandler<TEvent> handler) where TEvent : class;

    /// <summary>Dispatch an event and await all registered handlers.</summary>
    Task DispatchAsync<TEvent>(TEvent evt) where TEvent : class;

    /// <summary>Dispatch an event in a fire-and-forget manner with error logging.</summary>
    void DispatchAndForget<TEvent>(TEvent evt) where TEvent : class;
}

using System.Collections.Concurrent;
using ArayCode.Services;

namespace ArayCode;

/// <summary>
/// Default implementation of <see cref="IEventDispatcher"/>.
/// Stores handlers in a ConcurrentDictionary and supports both async and fire-and-forget dispatch.
/// </summary>
public class EventDispatcher : IEventDispatcher
{
    private readonly ConcurrentDictionary<Type, List<object>> _handlers = new();
    private readonly IColorConsole _console;

    public EventDispatcher(IColorConsole? console = null)
    {
        _console = console ?? new ColorConsole(new StreamShellHost());
    }

    public void RegisterHandler<TEvent>(IEventHandler<TEvent> handler) where TEvent : class
    {
        var handlers = _handlers.GetOrAdd(typeof(TEvent), _ => new List<object>());
        lock (handlers)
        {
            handlers.Add(handler);
        }
    }

    public async Task DispatchAsync<TEvent>(TEvent evt) where TEvent : class
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var handlerList))
            return;

        List<object> snapshot;
        lock (handlerList)
        {
            snapshot = new List<object>(handlerList);
        }

        foreach (var handler in snapshot)
        {
            if (handler is IEventHandler<TEvent> typedHandler)
            {
                await typedHandler.HandleAsync(evt);
            }
        }
    }

    public void DispatchAndForget<TEvent>(TEvent evt) where TEvent : class
    {
        _ = DispatchAndForgetInternal(evt);
    }

    private async Task DispatchAndForgetInternal<TEvent>(TEvent evt) where TEvent : class
    {
        try
        {
            await DispatchAsync(evt);
        }
        catch (Exception ex)
        {
            _console.LogError("EventDispatcher", $"Error dispatching {typeof(TEvent).Name}: {ex.Message}");
        }
    }
}

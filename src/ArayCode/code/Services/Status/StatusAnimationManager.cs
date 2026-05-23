using System;
using System.Threading;
using ArayCode.Services.StatusParts;

namespace ArayCode.Services;

/// <summary>
/// Manages the periodic animation timer for service status dots in the
/// yellow (transitional) state.  Exposes <see cref="AdvanceFrames"/> for
/// frame advancement under the owner's lock, and <see cref="EnsureRunning"/>
/// to start/stop the timer based on whether any part needs animation.
/// The <c>onTick</c> callback is invoked on a timer thread — the owner must
/// acquire its own synchronization before mutating shared state.
/// </summary>
public sealed class StatusAnimationManager : IDisposable
{
    private static readonly TimeSpan AnimationInterval = TimeSpan.FromMilliseconds(600);

    private readonly ServiceStatusPart[] _animatedParts;
    private readonly Action _onTick;
    private Timer? _timer;
    private bool _disposed;

    /// <summary>
    /// Creates a new animation manager.
    /// </summary>
    /// <param name="animatedParts">The service status parts that may be animated.</param>
    /// <param name="onTick">Callback invoked on a timer thread when a tick fires.
    /// The owner should acquire its own lock before mutating state.</param>
    public StatusAnimationManager(ServiceStatusPart[] animatedParts, Action onTick)
    {
        _animatedParts = animatedParts ?? throw new ArgumentNullException(nameof(animatedParts));
        _onTick = onTick ?? throw new ArgumentNullException(nameof(onTick));
    }

    /// <summary>
    /// Starts or restarts the animation timer if any service status part
    /// is in the yellow (transitional) state; stops it otherwise.
    /// Call whenever status colors change.  Must be called from within
    /// the owner's synchronization context.
    /// </summary>
    public void EnsureRunning()
    {
        ThrowIfDisposed();

        bool needsAnimation = false;
        foreach (var part in _animatedParts)
        {
            if (part.IsYellow)
            {
                needsAnimation = true;
                break;
            }
        }

        if (needsAnimation)
        {
            _timer ??= new Timer(OnTick, null, AnimationInterval, AnimationInterval);
        }
        else
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    /// <summary>
    /// Advances animation frames for all animated parts.
    /// Must be called from within the owner's lock.
    /// </summary>
    public void AdvanceFrames()
    {
        foreach (var part in _animatedParts)
        {
            part.AdvanceFrame();
        }
    }

    private void OnTick(object? state)
    {
        try
        {
            _onTick();
        }
        catch (ObjectDisposedException)
        {
            _timer?.Dispose();
            _timer = null;
        }
        catch
        {
            // Best-effort; never crash on timer callback
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(StatusAnimationManager));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer?.Dispose();
        _timer = null;
    }
}

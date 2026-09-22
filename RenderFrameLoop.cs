using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace Pickets;

/// <summary>One rendering subscription for the UI thread, active only while work is pending.
/// Animations run first; geometry-dependent chrome is refreshed once after all animations.</summary>
internal sealed class RenderFrameLoop : IDisposable
{
    private readonly Action<EventHandler> _subscribe;
    private readonly Action<EventHandler> _unsubscribe;
    private readonly List<Action<TimeSpan>> _animations = new();
    private readonly HashSet<Action> _pending = new();
    private TimeSpan? _lastFrame;
    private bool _subscribed;
    private bool _processing;
    private bool _disposed;

    internal RenderFrameLoop() : this(handler => CompositionTarget.Rendering += handler,
        handler => CompositionTarget.Rendering -= handler) { }

    internal RenderFrameLoop(Action<EventHandler> subscribe, Action<EventHandler> unsubscribe)
    {
        _subscribe = subscribe;
        _unsubscribe = unsubscribe;
    }

    internal void AddAnimation(Action<TimeSpan> update)
    {
        if (_disposed) return;
        if (!_animations.Contains(update)) _animations.Add(update);
        EnsureSubscribed();
    }

    internal void RemoveAnimation(Action<TimeSpan> update)
    {
        _animations.Remove(update);
        StopWhenIdle();
    }

    internal void Request(Action update)
    {
        if (_disposed) return;
        _pending.Add(update);
        EnsureSubscribed();
    }

    private void EnsureSubscribed()
    {
        if (_subscribed) return;
        _subscribed = true;
        _subscribe(OnRendering);
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is RenderingEventArgs rendering) ProcessFrame(rendering.RenderingTime);
    }

    internal void ProcessFrame(TimeSpan renderingTime)
    {
        // WPF can raise more than one callback for the same target frame during layout.
        if (_disposed || _processing || _lastFrame == renderingTime) return;
        _lastFrame = renderingTime;
        _processing = true;
        try
        {
            foreach (var animation in _animations.ToArray())
            {
                if (!_animations.Contains(animation)) continue;
                try { animation(renderingTime); }
                catch { _animations.Remove(animation); throw; }
            }
            var pending = _pending.ToArray();
            _pending.Clear();
            foreach (var update in pending)
            {
                if (_disposed) break;
                update();
            }
        }
        finally
        {
            _processing = false;
            StopWhenIdle();
        }
    }

    private void StopWhenIdle()
    {
        if (!_subscribed || _processing || _animations.Count != 0 || _pending.Count != 0) return;
        _unsubscribe(OnRendering);
        _subscribed = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _animations.Clear();
        _pending.Clear();
        if (_subscribed) _unsubscribe(OnRendering);
        _subscribed = false;
    }
}

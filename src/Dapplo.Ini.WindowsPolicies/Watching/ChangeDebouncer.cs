// Copyright (c) Dapplo. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace Dapplo.Ini.WindowsPolicies.Watching;

/// <summary>
/// Calls an action once, <see cref="Delay"/> after the last of a burst of <see cref="Trigger"/> calls.
/// </summary>
internal sealed class ChangeDebouncer : IDisposable
{
    private readonly Action _action;
    private readonly object _lock = new();
    private Timer? _timer;
    private bool _disposed;

    public ChangeDebouncer(TimeSpan delay, Action action)
    {
        Delay = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        _action = action ?? throw new ArgumentNullException(nameof(action));
    }

    public TimeSpan Delay { get; }

    /// <summary>(Re)starts the delay.</summary>
    public void Trigger()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _timer ??= new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(Delay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
        }
        try
        {
            _action();
        }
        catch
        {
            // A failing listener must not crash the timer thread.
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}

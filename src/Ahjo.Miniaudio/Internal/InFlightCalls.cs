namespace Ahjo.Miniaudio.Internal;

/// <summary>
/// Calls still running against a native object on an <see cref="AbandonableCall"/>
/// thread after their caller stopped waiting. They are using the object, so
/// its owner's <c>Dispose</c> hands the native release to this counter, which
/// runs it now if nothing is in flight, or when the last such call returns.
/// Disposal therefore never blocks on a backend, and never frees memory a
/// call is still using.
/// </summary>
internal sealed class InFlightCalls(Type owner)
{
    private readonly Lock _lock = new();
    private int _count;
    private bool _releaseRequested;
    private Action? _release;
    private volatile bool _released;

    /// <summary>A call is about to run against the object.</summary>
    /// <exception cref="ObjectDisposedException">The owner has been disposed.</exception>
    public void Begin()
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_releaseRequested, owner);
            _count++;
        }
    }

    /// <summary>That call returned; runs a deferred release if it was the last.</summary>
    public void End()
    {
        Action? release = null;
        lock (_lock)
        {
            if (--_count == 0)
            {
                release = _release;
                _release = null;
            }
        }

        if (release is not null)
        {
            Run(release);
        }
    }

    /// <summary>Runs <paramref name="release"/> now, or after the last call in flight returns.</summary>
    public void Release(Action release)
    {
        lock (_lock)
        {
            _releaseRequested = true;
            if (_count != 0)
            {
                _release = release;
                return;
            }
        }

        Run(release);
    }

    /// <summary>Whether the release has finished running (for tests).</summary>
    public bool IsReleased => _released;

    private void Run(Action release)
    {
        release();
        _released = true;
    }
}

namespace Ahjo.Miniaudio.Internal;

/// <summary>
/// The live native children of a native parent (devices and engines on a
/// context; sounds and groups on an engine). miniaudio requires children to be
/// uninitialized before their parent; the parent owns that order instead of
/// trusting the caller's, so a wrong disposal order is not a use-after-free.
/// </summary>
/// <remarks>Setup-time only: registering allocates a list node and takes a lock.</remarks>
internal sealed class ChildRegistry
{
    private readonly Lock _lock = new();
    private readonly LinkedList<IDisposable> _children = new();

    public LinkedListNode<IDisposable> Add(IDisposable child)
    {
        lock (_lock)
        {
            return _children.AddLast(child);
        }
    }

    public void Remove(LinkedListNode<IDisposable>? node)
    {
        if (node is null)
        {
            return;
        }

        lock (_lock)
        {
            // Already detached when DisposeAll took its snapshot.
            if (node.List is not null)
            {
                _children.Remove(node);
            }
        }
    }

    /// <summary>Disposes every live child, newest first (a sound before the group it was created into).</summary>
    public void DisposeAll()
    {
        IDisposable[] snapshot;
        lock (_lock)
        {
            snapshot = [.. _children];
            _children.Clear();
        }

        for (var i = snapshot.Length - 1; i >= 0; i--)
        {
            snapshot[i].Dispose();
        }
    }
}

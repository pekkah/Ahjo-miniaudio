using System.Runtime.InteropServices;

using Ahjo.Miniaudio.Internal;
using Ahjo.Miniaudio.Native;

namespace Ahjo.Miniaudio;

/// <summary>Options for <see cref="AudioContext.Create"/>. The default is valid.</summary>
public readonly record struct AudioContextDescription
{
    /// <summary>
    /// The backend to use. <see langword="null"/> (the default) tries the
    /// platform's backends in miniaudio's priority order (WASAPI first on
    /// Windows). <see cref="AudioBackend.Null"/> needs no audio hardware.
    /// </summary>
    public AudioBackend? Backend { get; init; }
}

/// <summary>
/// A backend connection (<c>ma_context</c>): enumerates devices, and is what
/// <see cref="AudioDevice"/> and <see cref="AudioEngine"/> open devices on.
/// </summary>
/// <remarks>
/// Optional: a device or engine created without one gets a private context on
/// the platform default backend. Create one to pick the backend or a specific
/// device.
/// <para><see cref="Dispose"/> first disposes every device and engine still
/// open on this context.</para>
/// </remarks>
public sealed unsafe class AudioContext : IDisposable
{
    private readonly ChildRegistry _children = new();
    private ma_context* _context;

    // Calls still running against the native context after their caller
    // stopped waiting (device and engine inits, enumerations). Dispose leaves
    // the native release to the last of them.
    private readonly InFlightCalls _calls = new(typeof(AudioContext));

    private AudioContext(ma_context* context) => _context = context;

    /// <summary>Initializes a context on the requested backend.</summary>
    /// <param name="description">Which backend.</param>
    /// <param name="cancellationToken">
    /// Stops waiting for the backend, e.g. a timeout from
    /// <see cref="CancellationTokenSource(TimeSpan)"/>. Some backends can block
    /// indefinitely: PulseAudio waits without a timeout for a server that
    /// accepted the connection and never answers. miniaudio cannot interrupt
    /// that wait, so cancelling abandons it: the init carries on on a
    /// background thread and is cleaned up whenever it returns — if it never
    /// does, one parked thread and the context's memory stay until the process
    /// exits. Without a token the call blocks the caller instead.
    /// </param>
    /// <exception cref="MiniaudioException">No requested backend could be initialized.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> fired before the backend answered.</exception>
    public static AudioContext Create(in AudioContextDescription description = default, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Opaque: its layout differs per platform, so the size comes from the binary.
        var context = (ma_context*)NativeBlock.Alloc(Ma.ahjo_ma_sizeof_ma_context());
        var backend = description.Backend;
        var result = AbandonableCall.Run(
            () => Init(backend, context),
            late =>
            {
                if (late == ma_result.MA_SUCCESS)
                {
                    Ma.ma_context_uninit(context);
                }

                NativeBlock.Free(context);
            },
            cancellationToken);

        if (result != ma_result.MA_SUCCESS)
        {
            NativeBlock.Free(context);
            throw new MiniaudioException(result, "ma_context_init");
        }

        return new AudioContext(context);
    }

    // Everything ma_context_init reads lives here, not in Create's frame,
    // which a cancelled caller has already left.
    private static ma_result Init(AudioBackend? backend, ma_context* context)
    {
        var config = Ma.ma_context_config_init();
        if (backend is { } requested)
        {
            var native = (ma_backend)requested;
            return Ma.ma_context_init(&native, 1, &config, context);
        }

        return Ma.ma_context_init(null, 0, &config, context);
    }

    /// <summary>The backend this context initialized.</summary>
    public AudioBackend Backend => (AudioBackend)Ma.ahjo_ma_context_get_backend(Native);

    /// <summary>
    /// The playback devices the backend reports. Allocates; call at setup or
    /// from a settings screen, not per frame.
    /// </summary>
    /// <param name="cancellationToken">
    /// Stops waiting for the backend; see <see cref="Create"/>. A cancelled
    /// enumeration carries on in the background and keeps the context's native
    /// state alive until it returns.
    /// </param>
    /// <exception cref="MiniaudioException">The backend could not enumerate its devices.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> fired before the backend answered.</exception>
    public AudioDeviceInfo[] GetPlaybackDevices(CancellationToken cancellationToken = default)
    {
        var context = Native;
        if (!cancellationToken.CanBeCanceled)
        {
            return Enumerate(context);
        }

        AudioDeviceInfo[]? devices = null;
        var result = AbandonableCall.Run(
            () =>
            {
                try
                {
                    devices = Enumerate(context);
                    return ma_result.MA_SUCCESS;
                }
                catch (MiniaudioException e)
                {
                    return e.Result;
                }
            },
            static _ => { },
            cancellationToken,
            _calls);
        MaCheck.ThrowIfFailed(result, "ma_context_get_devices");
        return devices!;
    }

    private static AudioDeviceInfo[] Enumerate(ma_context* context)
    {
        ma_device_info* infos;
        uint count;
        MaCheck.ThrowIfFailed(Ma.ma_context_get_devices(context, &infos, &count, null, null), "ma_context_get_devices");

        // The array belongs to the context and the next enumeration
        // overwrites it, so copy out now.
        var devices = new AudioDeviceInfo[count];
        for (var i = 0; i < devices.Length; i++)
        {
            var info = &infos[i];
            devices[i] = new AudioDeviceInfo(
                Marshal.PtrToStringUTF8((nint)(&info->name)) ?? string.Empty,
                new AudioDeviceId(in info->id),
                info->isDefault != 0);
        }

        return devices;
    }

    internal ma_context* Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_context == null, this);
            return _context;
        }
    }

    internal LinkedListNode<IDisposable> Register(IDisposable child) => _children.Add(child);

    internal void Unregister(LinkedListNode<IDisposable>? node) => _children.Remove(node);

    /// <summary>The calls in flight against this context; device and engine calls pass it as their outer owner.</summary>
    internal InFlightCalls Calls => _calls;

    /// <summary>Whether the native context is still allocated (for tests: a call in flight defers the release).</summary>
    internal bool IsNativeAlive => !_calls.IsReleased;

    /// <summary>Disposes every device and engine on this context, then the context.</summary>
    public void Dispose()
    {
        if (_context == null)
        {
            return;
        }

        _children.DisposeAll();

        var context = _context;
        _context = null;
        _calls.Release(() => Release(context));
    }

    private static void Release(ma_context* context)
    {
        Ma.ma_context_uninit(context);
        NativeBlock.Free(context);
    }
}

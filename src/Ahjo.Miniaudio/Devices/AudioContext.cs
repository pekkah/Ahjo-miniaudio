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

    private AudioContext(ma_context* context) => _context = context;

    /// <summary>Initializes a context on the requested backend.</summary>
    /// <exception cref="MiniaudioException">No requested backend could be initialized.</exception>
    public static AudioContext Create(in AudioContextDescription description = default)
    {
        var context = NativeBlock.Alloc<ma_context>();
        var config = Ma.ma_context_config_init();
        ma_result result;
        if (description.Backend is { } backend)
        {
            var native = (ma_backend)backend;
            result = Ma.ma_context_init(&native, 1, &config, context);
        }
        else
        {
            result = Ma.ma_context_init(null, 0, &config, context);
        }

        if (result != ma_result.MA_SUCCESS)
        {
            NativeBlock.Free(context);
            throw new MiniaudioException(result, "ma_context_init");
        }

        return new AudioContext(context);
    }

    /// <summary>The backend this context initialized.</summary>
    public AudioBackend Backend => (AudioBackend)Native->backend;

    /// <summary>
    /// The playback devices the backend reports. Allocates; call at setup or
    /// from a settings screen, not per frame.
    /// </summary>
    public AudioDeviceInfo[] GetPlaybackDevices()
    {
        ma_device_info* infos;
        uint count;
        MaCheck.ThrowIfFailed(Ma.ma_context_get_devices(Native, &infos, &count, null, null), "ma_context_get_devices");

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

    /// <summary>Disposes every device and engine on this context, then the context.</summary>
    public void Dispose()
    {
        if (_context == null)
        {
            return;
        }

        _children.DisposeAll();
        Ma.ma_context_uninit(_context);
        NativeBlock.Free(_context);
        _context = null;
    }
}

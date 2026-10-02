using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Ahjo.Miniaudio.Internal;
using Ahjo.Miniaudio.Native;

namespace Ahjo.Miniaudio;

/// <summary>Options for <see cref="AudioDevice.Create"/>. The default is valid: the default device at its native format.</summary>
public readonly record struct AudioDeviceDescription
{
    /// <summary>The device to open; <see langword="null"/> is the system default (and follows it when the default changes).</summary>
    public AudioDeviceId? PlaybackDevice { get; init; }

    /// <summary>Output channels; 0 uses the device's native count.</summary>
    public int Channels { get; init; }

    /// <summary>Output sample rate in Hz; 0 uses the device's native rate.</summary>
    public int SampleRate { get; init; }

    /// <summary>Frames per render callback; 0 lets miniaudio pick a low-latency period.</summary>
    public int PeriodSizeInFrames { get; init; }

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(Channels, nameof(Channels));
        ArgumentOutOfRangeException.ThrowIfNegative(SampleRate, nameof(SampleRate));
        ArgumentOutOfRangeException.ThrowIfNegative(PeriodSizeInFrames, nameof(PeriodSizeInFrames));
    }
}

/// <summary>
/// A playback device (<c>ma_device</c>) whose audio thread pulls interleaved
/// <c>f32</c> frames from an <see cref="IAudioRenderer"/>. This is the "buffers
/// out" tier: anything that renders its own audio — a DSP graph, or an
/// <see cref="AudioEngine"/> created with <see cref="AudioEngineDescription.NoDevice"/> —
/// plays through one.
/// </summary>
/// <remarks>
/// <para>Created stopped; call <see cref="Start"/>.</para>
/// <para>The render path allocates nothing: miniaudio's thread reaches this
/// object through a normal (non-pinning) GC handle, and the renderer sees the
/// native buffer as a <see cref="Span{T}"/>.</para>
/// <para><b>Dispose it.</b> A running device's thread keeps it reachable, so a
/// forgotten device plays until the process exits. Disposing its
/// <see cref="AudioContext"/> disposes it too.</para>
/// </remarks>
public sealed unsafe class AudioDevice : IDisposable
{
    private readonly IAudioRenderer _renderer;
    private readonly AudioContext? _context;
    private readonly LinkedListNode<IDisposable>? _registration;
    private ma_device* _device;
    private GCHandle<AudioDevice> _self;
    private Exception? _fault;

    private AudioDevice(AudioContext? context, in AudioDeviceDescription description, IAudioRenderer renderer)
    {
        _renderer = renderer;
        _device = NativeBlock.Alloc<ma_device>();
        _self = new GCHandle<AudioDevice>(this);
        try
        {
            var config = Ma.ma_device_config_init(ma_device_type.ma_device_type_playback);
            config.playback.format = ma_format.ma_format_f32;
            config.playback.channels = (uint)description.Channels;
            config.sampleRate = (uint)description.SampleRate;
            config.periodSizeInFrames = (uint)description.PeriodSizeInFrames;
            config.dataCallback = &OnData;
            config.notificationCallback = &OnNotification;
            config.pUserData = (void*)GCHandle<AudioDevice>.ToIntPtr(_self);

            ma_device_id id;
            if (description.PlaybackDevice is { } device)
            {
                id = device.Native;
                config.playback.pDeviceID = &id;
            }

            MaCheck.ThrowIfFailed(Ma.ma_device_init(context is null ? null : context.Native, &config, _device), "ma_device_init");
        }
        catch
        {
            _self.Dispose();
            NativeBlock.Free(_device);
            _device = null;
            throw;
        }

        Channels = (int)_device->playback.channels;
        SampleRate = (int)_device->sampleRate;
        _context = context;
        _registration = context?.Register(this);
    }

    /// <summary>Opens a playback device that renders through <paramref name="renderer"/>.</summary>
    /// <param name="context">The backend to open it on; <see langword="null"/> for the platform default.</param>
    /// <param name="description">Which device, and the format to request.</param>
    /// <param name="renderer">Called on the audio thread once per period.</param>
    /// <exception cref="MiniaudioException">The backend could not open the device.</exception>
    public static AudioDevice Create(AudioContext? context, in AudioDeviceDescription description, IAudioRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        description.Validate();
        return new AudioDevice(context, description, renderer);
    }

    /// <summary>The channel count the device actually opened with — what <see cref="IAudioRenderer.Render"/> receives.</summary>
    public int Channels { get; }

    /// <summary>The sample rate the device actually opened with, in Hz.</summary>
    public int SampleRate { get; }

    /// <summary>Whether the device is running.</summary>
    public bool IsStarted => Ma.ma_device_is_started(Native) != 0;

    /// <summary>
    /// The first exception the renderer threw, or <see langword="null"/>. Once
    /// set, the device plays silence and no longer calls the renderer; dispose
    /// and recreate it to recover.
    /// </summary>
    public Exception? Fault => Volatile.Read(ref _fault);

    /// <summary>Starts the audio thread pulling from the renderer.</summary>
    public void Start() => MaCheck.ThrowIfFailed(Ma.ma_device_start(Native), "ma_device_start");

    /// <summary>Stops the device; returns once the render callback is no longer running.</summary>
    public void Stop() => MaCheck.ThrowIfFailed(Ma.ma_device_stop(Native), "ma_device_stop");

    private ma_device* Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_device == null, this);
            return _device;
        }
    }

    /// <summary>Stops and closes the device.</summary>
    public void Dispose()
    {
        var device = _device;
        if (device == null)
        {
            return;
        }

        _device = null;
        _context?.Unregister(_registration);

        // Uninit stops the thread and still delivers the Stopped
        // notification through pUserData, so the handle must outlive it.
        Ma.ma_device_uninit(device);
        NativeBlock.Free(device);
        _self.Dispose();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnData(ma_device* device, void* output, void* input, uint frameCount)
    {
        var self = GCHandle<AudioDevice>.FromIntPtr((nint)device->pUserData).Target;
        if (Volatile.Read(ref self._fault) is not null)
        {
            return; // the buffer arrives silenced
        }

        var channels = (int)device->playback.channels;
        var buffer = new Span<float>(output, (int)frameCount * channels);
        try
        {
            self._renderer.Render(buffer, channels);
        }
        catch (Exception e)
        {
            buffer.Clear();
            Interlocked.CompareExchange(ref self._fault, e, null);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnNotification(ma_device_notification* notification)
    {
        var self = GCHandle<AudioDevice>.FromIntPtr((nint)notification->pDevice->pUserData).Target;
        try
        {
            self._renderer.OnNotification((AudioDeviceNotification)notification->type);
        }
        catch (Exception e)
        {
            Interlocked.CompareExchange(ref self._fault, e, null);
        }
    }
}

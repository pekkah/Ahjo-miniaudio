using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Ahjo.Miniaudio.Internal;
using Ahjo.Miniaudio.Native;

namespace Ahjo.Miniaudio;

/// <summary>Options for <see cref="AudioEngine.Create"/>. The default is valid: the default device, started.</summary>
public readonly record struct AudioEngineDescription
{
    /// <summary>The backend to open the device on; <see langword="null"/> for the platform default. Ignored with <see cref="NoDevice"/>.</summary>
    public AudioContext? Context { get; init; }

    /// <summary>The device to play on; <see langword="null"/> is the system default. Ignored with <see cref="NoDevice"/>.</summary>
    public AudioDeviceId? PlaybackDevice { get; init; }

    /// <summary>
    /// Open no device: the engine mixes only when <see cref="AudioEngine.Read"/>
    /// pulls it — typically from an <see cref="IAudioRenderer"/> on an
    /// <see cref="AudioDevice"/>, alongside other audio, or from a test.
    /// </summary>
    public bool NoDevice { get; init; }

    /// <summary>Mix channels; 0 uses the device's (or 2 with <see cref="NoDevice"/>).</summary>
    public int Channels { get; init; }

    /// <summary>Mix rate in Hz; 0 uses the device's (or 48000 with <see cref="NoDevice"/>).</summary>
    public int SampleRate { get; init; }

    /// <summary>Device period in frames; 0 lets miniaudio pick.</summary>
    public int PeriodSizeInFrames { get; init; }

    /// <summary>Listeners to spatialize against, 1 to 4; 0 means 1.</summary>
    public int ListenerCount { get; init; }

    /// <summary>Leave the device stopped until <see cref="AudioEngine.Start"/>.</summary>
    public bool NoAutoStart { get; init; }

    /// <summary>
    /// Receives the engine's device notifications: reroutes, and a
    /// <see cref="AudioDeviceNotification.Stopped"/> you did not ask for when
    /// the backend loses the device. Not allowed with <see cref="NoDevice"/>
    /// (there is no device to observe; a pulled engine's device is the
    /// caller's <see cref="AudioDevice"/>, observed through its renderer).
    /// </summary>
    public IAudioDeviceObserver? DeviceObserver { get; init; }

    internal void Validate()
    {
        if (NoDevice && DeviceObserver is not null)
        {
            throw new ArgumentException(
                "A NoDevice engine opens no device, so a DeviceObserver would never be called. " +
                "Observe the AudioDevice that pulls the engine through its IAudioRenderer instead.",
                nameof(DeviceObserver));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(Channels, nameof(Channels));
        ArgumentOutOfRangeException.ThrowIfNegative(SampleRate, nameof(SampleRate));
        ArgumentOutOfRangeException.ThrowIfNegative(PeriodSizeInFrames, nameof(PeriodSizeInFrames));
        ArgumentOutOfRangeException.ThrowIfNegative(ListenerCount, nameof(ListenerCount));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ListenerCount, AudioEngine.MaxListeners, nameof(ListenerCount));
    }
}

/// <summary>
/// miniaudio's mixer and spatializer (<c>ma_engine</c>): plays <see cref="Sound"/>s
/// through <see cref="SoundGroup"/> buses, spatialized against up to four
/// <see cref="AudioListener"/>s.
/// </summary>
/// <remarks>
/// <para>Either owns a device and plays on it, or — with
/// <see cref="AudioEngineDescription.NoDevice"/> — mixes only when
/// <see cref="Read"/> pulls it.</para>
/// <para><b>Threading.</b> Sound, group and listener controls are safe to call
/// while the audio thread mixes; drive each object from one thread at a time
/// (the game thread). Creating and disposing objects is setup-time work.</para>
/// <para><see cref="Dispose"/> first disposes every sound and group still
/// alive on the engine, newest first.</para>
/// </remarks>
public sealed unsafe class AudioEngine : IDisposable
{
    /// <summary>The most listeners an engine supports (<c>MA_ENGINE_MAX_LISTENERS</c>).</summary>
    public const int MaxListeners = 4;

    private readonly ChildRegistry _children = new();
    private readonly AudioContext? _context;
    private readonly LinkedListNode<IDisposable>? _registration;
    private readonly bool _noDevice;
    private readonly IAudioDeviceObserver? _observer;
    private GCHandle<AudioEngine> _self;
    private ma_engine* _engine;
    private Exception? _fault;

    private AudioEngine(in AudioEngineDescription description)
    {
        _noDevice = description.NoDevice;
        _observer = description.DeviceObserver;
        var context = description.NoDevice ? null : description.Context;
        var config = BuildConfig(description, context);

        ma_device_id id;
        if (!description.NoDevice && description.PlaybackDevice is { } device)
        {
            id = device.Native;
            config.pPlaybackDeviceID = &id;
        }

        _engine = NativeBlock.Alloc<ma_engine>();
        if (_observer is not null)
        {
            // The engine's device carries pUserData = the ma_engine, not us,
            // so the way back to this object is ma_engine.pProcessUserData:
            // miniaudio stores it before creating the device (notifications
            // arrive during init when the engine starts itself) and reads it
            // only to call onProcess, which stays null. Everything
            // OnNotification touches is assigned above this point.
            _self = new GCHandle<AudioEngine>(this);
            config.pProcessUserData = (void*)GCHandle<AudioEngine>.ToIntPtr(_self);
            config.notificationCallback = &OnNotification;
        }

        var result = Ma.ma_engine_init(&config, _engine);
        if (result != ma_result.MA_SUCCESS)
        {
            NativeBlock.Free(_engine);
            _engine = null;
            if (_self.IsAllocated)
            {
                _self.Dispose();
            }

            throw new MiniaudioException(result, "ma_engine_init");
        }

        Channels = (int)Ma.ma_engine_get_channels(_engine);
        SampleRate = (int)Ma.ma_engine_get_sample_rate(_engine);
        ListenerCount = (int)Ma.ma_engine_get_listener_count(_engine);
        _context = context;
        _registration = context?.Register(this);
    }

    /// <summary>Creates an engine.</summary>
    /// <exception cref="ArgumentException">A <see cref="AudioEngineDescription.DeviceObserver"/> on a <see cref="AudioEngineDescription.NoDevice"/> engine.</exception>
    /// <exception cref="MiniaudioException">The device could not be opened, or the engine could not be initialized.</exception>
    public static AudioEngine Create(in AudioEngineDescription description = default)
    {
        description.Validate();
        return new AudioEngine(description);
    }

    private static ma_engine_config BuildConfig(in AudioEngineDescription description, AudioContext? context)
    {
        var config = Ma.ma_engine_config_init();
        if (description.NoDevice)
        {
            // ma_engine_init has no defaults without a device (it fails with
            // MA_INVALID_ARGS); supply them so the default description works.
            config.noDevice = 1;
            config.channels = (uint)(description.Channels == 0 ? 2 : description.Channels);
            config.sampleRate = (uint)(description.SampleRate == 0 ? 48000 : description.SampleRate);
        }
        else
        {
            config.pContext = context is null ? null : context.Native;
            config.channels = (uint)description.Channels;
            config.sampleRate = (uint)description.SampleRate;
        }

        config.periodSizeInFrames = (uint)description.PeriodSizeInFrames;
        config.listenerCount = (uint)description.ListenerCount;
        config.noAutoStart = Units.Bool(description.NoAutoStart);
        return config;
    }

    /// <summary>Channels in the mix — what <see cref="Read"/> interleaves.</summary>
    public int Channels { get; }

    /// <summary>The mix rate in Hz.</summary>
    public int SampleRate { get; }

    /// <summary>How many listeners the engine spatializes against.</summary>
    public int ListenerCount { get; }

    /// <summary>
    /// The engine's clock in frames at <see cref="SampleRate"/>: how much it
    /// has mixed. Advances on the audio thread (or with each <see cref="Read"/>)
    /// — but only while at least one sound or group exists on the engine:
    /// miniaudio's clock is its output node's, and an output with nothing
    /// attached mixes nothing.
    /// </summary>
    public ulong TimeInFrames => Ma.ma_engine_get_time_in_pcm_frames(Native);

    /// <summary>Master volume, linear (1 = unity).</summary>
    public float Volume
    {
        get => Ma.ma_engine_get_volume(Native);
        set => MaCheck.ThrowIfFailed(Ma.ma_engine_set_volume(Native, value), "ma_engine_set_volume");
    }

    /// <summary>
    /// Whether the engine's device is running; always <see langword="false"/>
    /// for a <see cref="AudioEngineDescription.NoDevice"/> engine. The polling
    /// counterpart of <see cref="AudioEngineDescription.DeviceObserver"/>: a
    /// game thread that did not call <see cref="Stop"/> and reads
    /// <see langword="false"/> here has lost its device.
    /// </summary>
    public bool IsStarted
    {
        get
        {
            var device = Ma.ma_engine_get_device(Native);
            return device != null && Ma.ma_device_is_started(device) != 0;
        }
    }

    /// <summary>
    /// The first exception the <see cref="AudioEngineDescription.DeviceObserver"/>
    /// threw, or <see langword="null"/>. Once set, the observer is no longer
    /// called; the engine keeps playing.
    /// </summary>
    public Exception? Fault => Volatile.Read(ref _fault);

    /// <summary>The first listener — the only one unless <see cref="AudioEngineDescription.ListenerCount"/> says otherwise.</summary>
    public AudioListener Listener => GetListener(0);

    /// <summary>Listener <paramref name="index"/>, 0 to <see cref="ListenerCount"/> − 1.</summary>
    public AudioListener GetListener(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, ListenerCount);
        _ = Native;
        return new AudioListener(this, index);
    }

    /// <summary>Starts the engine's device.</summary>
    public void Start() => MaCheck.ThrowIfFailed(Ma.ma_engine_start(Native), "ma_engine_start");

    /// <summary>Stops the engine's device; sounds keep their state and resume on <see cref="Start"/>.</summary>
    public void Stop() => MaCheck.ThrowIfFailed(Ma.ma_engine_stop(Native), "ma_engine_stop");

    /// <summary>
    /// Mixes <c>output.Length / Channels</c> frames into <paramref name="output"/>
    /// (interleaved, overwriting it) and advances the engine's clock. Allocates
    /// nothing; safe to call from an <see cref="IAudioRenderer"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The engine owns a device (no <see cref="AudioEngineDescription.NoDevice"/>).</exception>
    /// <exception cref="ArgumentException"><paramref name="output"/> is not a whole number of frames.</exception>
    public void Read(Span<float> output)
    {
        var engine = Native;
        if (!_noDevice)
        {
            throw new InvalidOperationException("The engine plays on its own device; Read is only for an engine created with NoDevice.");
        }

        if (output.Length % Channels != 0)
        {
            throw new ArgumentException($"{output.Length} samples is not a whole number of {Channels}-channel frames.", nameof(output));
        }

        fixed (float* p = output)
        {
            MaCheck.ThrowIfFailed(Ma.ma_engine_read_pcm_frames(engine, p, (ulong)(output.Length / Channels), null), "ma_engine_read_pcm_frames");
        }
    }

    internal ma_engine* Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_engine == null, this);
            return _engine;
        }
    }

    internal LinkedListNode<IDisposable> Register(IDisposable child) => _children.Add(child);

    internal void Unregister(LinkedListNode<IDisposable>? node) => _children.Remove(node);

    /// <summary>
    /// Disposes every sound and group on the engine, then the engine and its device.
    /// </summary>
    /// <remarks>
    /// A <see cref="AudioEngineDescription.NoDevice"/> engine must not be
    /// disposed while a device's renderer can still <see cref="Read"/> it:
    /// dispose (or stop) that device first.
    /// </remarks>
    public void Dispose()
    {
        if (_engine == null)
        {
            return;
        }

        _children.DisposeAll();
        _context?.Unregister(_registration);

        // Uninit stops the device, which still delivers Stopped through the
        // handle, so the handle must outlive it.
        Ma.ma_engine_uninit(_engine);
        NativeBlock.Free(_engine);
        _engine = null;
        if (_self.IsAllocated)
        {
            _self.Dispose();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnNotification(ma_device_notification* notification)
    {
        var engine = (ma_engine*)notification->pDevice->pUserData;
        var self = GCHandle<AudioEngine>.FromIntPtr((nint)engine->pProcessUserData).Target;
        if (Volatile.Read(ref self._fault) is not null)
        {
            return;
        }

        try
        {
            self._observer!.OnNotification((AudioDeviceNotification)notification->type);
        }
        catch (Exception e)
        {
            Interlocked.CompareExchange(ref self._fault, e, null);
        }
    }
}

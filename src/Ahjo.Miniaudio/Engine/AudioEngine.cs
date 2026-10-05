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
/// <para><b>Dispose it.</b> An engine's device plays until it is disposed,
/// and an engine with a <see cref="AudioEngineDescription.DeviceObserver"/>
/// is kept reachable by its device (through a GC handle) until then, so a
/// forgotten engine is never collected.</para>
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

    // Calls (Start, Stop) still running on an AbandonableCall thread after
    // their caller stopped waiting; Dispose defers the native release to them.
    private readonly InFlightCalls _calls = new(typeof(AudioEngine));

    // Set when the caller no longer owns a running engine: its Create was
    // cancelled, or its Dispose had to defer the uninit. Notifications then
    // stay away from the observer.
    private volatile bool _closed;

    // Reads in flight on another thread (a device's audio thread pulling a
    // NoDevice engine). Dispose waits for it to drain before freeing.
    private int _readers;

    private AudioEngine(in AudioEngineDescription description, CancellationToken cancellationToken)
    {
        _noDevice = description.NoDevice;
        _observer = description.DeviceObserver;
        var context = description.NoDevice ? null : description.Context;
        var contextNative = context is null ? null : context.Native;
        var engine = NativeBlock.Alloc<ma_engine>();
        GCHandle<AudioEngine> self = default;
        if (_observer is not null)
        {
            // The engine's device carries pUserData = the ma_engine, not us,
            // so the way back to this object is ma_engine.pProcessUserData:
            // miniaudio stores it before creating the device (notifications
            // arrive during init when the engine starts itself) and reads it
            // only to call onProcess, which stays null. Everything
            // OnNotification touches is assigned above this point.
            self = new GCHandle<AudioEngine>(this);
        }

        var copy = description;
        ma_result result;
        try
        {
            result = AbandonableCall.Run(
                () => Init(copy, contextNative, self, engine),
                late =>
                {
                    // A late success may already be delivering notifications
                    // through the handle, so it outlives the uninit.
                    if (late == ma_result.MA_SUCCESS)
                    {
                        Ma.ma_engine_uninit(engine);
                    }

                    NativeBlock.Free(engine);
                    if (self.IsAllocated)
                    {
                        self.Dispose();
                    }
                },
                cancellationToken,
                outer: context?.Calls);
        }
        catch (OperationCanceledException)
        {
            // The init may still succeed on its thread and notify through
            // the handle before it is uninitialized; keep those
            // notifications away from an object the caller never received.
            _closed = true;
            throw;
        }

        if (result != ma_result.MA_SUCCESS)
        {
            NativeBlock.Free(engine);
            if (self.IsAllocated)
            {
                self.Dispose();
            }

            throw new MiniaudioException(result, "ma_engine_init");
        }

        _engine = engine;
        _self = self;
        Channels = (int)Ma.ma_engine_get_channels(engine);
        SampleRate = (int)Ma.ma_engine_get_sample_rate(engine);
        ListenerCount = (int)Ma.ma_engine_get_listener_count(engine);
        _context = context;
        _registration = context?.Register(this);
    }

    /// <summary>Creates an engine.</summary>
    /// <param name="description">The device, format and listeners.</param>
    /// <param name="cancellationToken">
    /// Stops waiting for the backend while the engine opens its device (and,
    /// without a <see cref="AudioEngineDescription.Context"/>, its private
    /// context); see <see cref="AudioContext.Create"/> for what cancelling does
    /// and does not do. A <see cref="AudioEngineDescription.NoDevice"/> engine
    /// never waits on a backend. An abandoned engine that opens its device
    /// after all is silenced toward its <see cref="AudioEngineDescription.DeviceObserver"/>,
    /// but a notification that lands just as the token fires can still reach it.
    /// </param>
    /// <exception cref="ArgumentException">A <see cref="AudioEngineDescription.DeviceObserver"/> on a <see cref="AudioEngineDescription.NoDevice"/> engine.</exception>
    /// <exception cref="MiniaudioException">The device could not be opened, or the engine could not be initialized.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> fired before the backend answered.</exception>
    public static AudioEngine Create(in AudioEngineDescription description = default, CancellationToken cancellationToken = default)
    {
        description.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        return new AudioEngine(description, cancellationToken);
    }

    // Everything ma_engine_init reads lives here, not in the constructor's
    // frame, which a cancelled caller has already left.
    private static ma_result Init(AudioEngineDescription description, ma_context* context, GCHandle<AudioEngine> self, ma_engine* engine)
    {
        var config = BuildConfig(description, context);

        ma_device_id id;
        if (!description.NoDevice && description.PlaybackDevice is { } device)
        {
            id = device.Native;
            config.pPlaybackDeviceID = &id;
        }

        if (self.IsAllocated)
        {
            config.pProcessUserData = (void*)GCHandle<AudioEngine>.ToIntPtr(self);
            config.notificationCallback = &OnNotification;
        }

        return Ma.ma_engine_init(&config, engine);
    }

    private static ma_engine_config BuildConfig(in AudioEngineDescription description, ma_context* context)
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
            config.pContext = context;
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
    /// <param name="cancellationToken">
    /// Stops waiting for the backend; see <see cref="AudioContext.Create"/>.
    /// A cancelled start carries on in the background, so the device may
    /// still start once the backend answers.
    /// </param>
    /// <exception cref="InvalidOperationException">The engine has no device (<see cref="AudioEngineDescription.NoDevice"/>).</exception>
    /// <exception cref="MiniaudioException">The backend could not start the device.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> fired before the backend answered.</exception>
    public void Start(CancellationToken cancellationToken = default)
    {
        var engine = DeviceEngine(nameof(Start));
        if (!cancellationToken.CanBeCanceled)
        {
            MaCheck.ThrowIfFailed(Ma.ma_engine_start(engine), "ma_engine_start");
            return;
        }

        Control(engine, start: true, cancellationToken);
    }

    /// <summary>Stops the engine's device; sounds keep their state and resume on <see cref="Start"/>.</summary>
    /// <param name="cancellationToken">
    /// Stops waiting for the backend; see <see cref="AudioContext.Create"/>.
    /// A cancelled stop carries on in the background.
    /// </param>
    /// <exception cref="InvalidOperationException">The engine has no device (<see cref="AudioEngineDescription.NoDevice"/>).</exception>
    /// <exception cref="MiniaudioException">The backend could not stop the device.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> fired before the backend answered.</exception>
    public void Stop(CancellationToken cancellationToken = default)
    {
        var engine = DeviceEngine(nameof(Stop));
        if (!cancellationToken.CanBeCanceled)
        {
            MaCheck.ThrowIfFailed(Ma.ma_engine_stop(engine), "ma_engine_stop");
            return;
        }

        Control(engine, start: false, cancellationToken);
    }

    // Separate from Start/Stop so their no-token path allocates no closure.
    private void Control(ma_engine* engine, bool start, CancellationToken cancellationToken)
    {
        var result = AbandonableCall.Run(
            () => start ? Ma.ma_engine_start(engine) : Ma.ma_engine_stop(engine),
            static _ => { },
            cancellationToken,
            _calls,
            _context?.Calls);
        MaCheck.ThrowIfFailed(result, start ? "ma_engine_start" : "ma_engine_stop");
    }

    // miniaudio answers a deviceless start/stop with MA_INVALID_OPERATION,
    // which would surface as a MiniaudioException — a native failure — for
    // what is a caller error.
    private ma_engine* DeviceEngine(string member)
    {
        var engine = Native;
        if (_noDevice)
        {
            throw new InvalidOperationException(
                $"{member} controls the engine's own device; a NoDevice engine plays when its AudioDevice is started.");
        }

        return engine;
    }

    /// <summary>
    /// Mixes <c>output.Length / Channels</c> frames into <paramref name="output"/>
    /// (interleaved, overwriting it) and advances the engine's clock. Allocates
    /// nothing; safe to call from an <see cref="IAudioRenderer"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The engine owns a device (no <see cref="AudioEngineDescription.NoDevice"/>).</exception>
    /// <exception cref="ArgumentException"><paramref name="output"/> is not a whole number of frames.</exception>
    public void Read(Span<float> output)
    {
        if (!_noDevice)
        {
            throw new InvalidOperationException("The engine plays on its own device; Read is only for an engine created with NoDevice.");
        }

        if (output.Length % Channels != 0)
        {
            throw new ArgumentException($"{output.Length} samples is not a whole number of {Channels}-channel frames.", nameof(output));
        }

        // Announce the read before looking at _engine (the increment is a
        // full fence). Dispose clears _engine, fences, then waits for
        // _readers to drain — so either this read sees the engine disposed,
        // or Dispose waits for it to finish before freeing anything.
        Interlocked.Increment(ref _readers);
        try
        {
            var engine = Native;
            fixed (float* p = output)
            {
                MaCheck.ThrowIfFailed(Ma.ma_engine_read_pcm_frames(engine, p, (ulong)(output.Length / Channels), null), "ma_engine_read_pcm_frames");
            }
        }
        finally
        {
            Interlocked.Decrement(ref _readers);
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
    /// Safe while another thread is inside <see cref="Read"/> (a device
    /// pulling a <see cref="AudioEngineDescription.NoDevice"/> engine): it
    /// waits for that read to finish, and later reads throw
    /// <see cref="ObjectDisposedException"/>, which an <see cref="AudioDevice"/>
    /// latches into its <see cref="AudioDevice.Fault"/> and answers with
    /// silence. Disposing the device first avoids that fault.
    /// </remarks>
    public void Dispose()
    {
        var engine = _engine;
        if (engine == null)
        {
            return;
        }

        _children.DisposeAll();
        _context?.Unregister(_registration);

        // Unpublish, then drain the readers that got in before (see Read).
        _engine = null;
        Interlocked.MemoryBarrier();
        var spin = default(SpinWait);
        while (Volatile.Read(ref _readers) != 0)
        {
            spin.SpinOnce();
        }

        // Uninit stops the device, which still delivers Stopped through the
        // handle, so the handle must outlive it. A cancelled Start or Stop
        // still waiting on the backend defers all of it to that call.
        var self = _self;
        var releasedNow = _calls.Release(() =>
        {
            Ma.ma_engine_uninit(engine);
            NativeBlock.Free(engine);
            if (self.IsAllocated)
            {
                self.Dispose();
            }
        });
        if (!releasedNow)
        {
            _closed = true;
        }
    }

    /// <summary>The calls in flight against this engine (for tests).</summary>
    internal InFlightCalls Calls => _calls;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnNotification(ma_device_notification* notification)
    {
        var engine = (ma_engine*)Ma.ahjo_ma_device_get_user_data(notification->pDevice);
        var self = GCHandle<AudioEngine>.FromIntPtr((nint)engine->pProcessUserData).Target;
        if (self._closed || Volatile.Read(ref self._fault) is not null)
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

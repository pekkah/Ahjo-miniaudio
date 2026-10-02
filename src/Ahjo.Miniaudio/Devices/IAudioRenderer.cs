namespace Ahjo.Miniaudio;

/// <summary>
/// Receives a playback device's state changes: an <see cref="AudioDevice"/>'s
/// (through its <see cref="IAudioRenderer"/>), or the device an
/// <see cref="AudioEngine"/> opens for itself
/// (<see cref="AudioEngineDescription.DeviceObserver"/>).
/// </summary>
public interface IAudioDeviceObserver
{
    /// <summary>
    /// A device state change. <see cref="AudioDeviceNotification.Rerouted"/>
    /// means the default endpoint changed and playback moved with it. A
    /// <see cref="AudioDeviceNotification.Stopped"/> you did not ask for means
    /// the backend lost the device: <c>Start</c> may revive it, otherwise
    /// recreate the device or engine.
    /// </summary>
    /// <remarks>
    /// <para>Runs on a miniaudio thread, possibly concurrently with rendering,
    /// and also synchronously: from inside <c>Create</c> (an engine that
    /// starts itself reports <see cref="AudioDeviceNotification.Started"/>
    /// before <c>Create</c> returns), <c>Start</c>, <c>Stop</c> and
    /// <c>Dispose</c>. Record what happened and act on it from your own
    /// thread; never call back into the device or engine from here — during
    /// <c>Dispose</c> it is half torn down.</para>
    /// <para>An exception thrown here does not escape to miniaudio. It is
    /// recorded in the owner's <c>Fault</c>, and the observer is not called
    /// again.</para>
    /// </remarks>
    void OnNotification(AudioDeviceNotification notification);
}

/// <summary>
/// Produces the audio an <see cref="AudioDevice"/> plays. Both members run on
/// miniaudio's threads, never the thread that created the device.
/// </summary>
public interface IAudioRenderer : IAudioDeviceObserver
{
    /// <summary>
    /// Fill <paramref name="output"/> with interleaved <c>f32</c> samples:
    /// <c>output.Length / channels</c> frames of <paramref name="channels"/>
    /// samples each. The buffer arrives silenced, so a renderer with nothing to
    /// play may return without writing.
    /// </summary>
    /// <remarks>
    /// <para>Runs on the real-time audio thread once per device period. It must
    /// not allocate, block, take a lock that a non-real-time thread can hold, or
    /// do I/O: any of those is an audible glitch when it stalls.</para>
    /// <para>An exception thrown here does not escape to miniaudio (that would
    /// kill the process). The device catches it, plays silence, records it in
    /// <see cref="AudioDevice.Fault"/>, and stops calling this renderer.</para>
    /// </remarks>
    void Render(Span<float> output, int channels);

    /// <inheritdoc />
    /// <remarks>A renderer that does not care about device state need not implement this.</remarks>
    void IAudioDeviceObserver.OnNotification(AudioDeviceNotification notification)
    {
    }
}

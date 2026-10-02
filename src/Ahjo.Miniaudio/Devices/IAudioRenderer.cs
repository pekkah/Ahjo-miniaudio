namespace Ahjo.Miniaudio;

/// <summary>
/// Produces the audio an <see cref="AudioDevice"/> plays. Both members run on
/// miniaudio's threads, never the thread that created the device.
/// </summary>
public interface IAudioRenderer
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

    /// <summary>
    /// A device state change, e.g. <see cref="AudioDeviceNotification.Rerouted"/>
    /// when the default endpoint changes. Runs on a miniaudio thread, possibly
    /// concurrently with <see cref="Render"/>, and also from inside
    /// <see cref="AudioDevice.Stop"/> and <see cref="AudioDevice.Dispose"/>.
    /// </summary>
    void OnNotification(AudioDeviceNotification notification)
    {
    }
}

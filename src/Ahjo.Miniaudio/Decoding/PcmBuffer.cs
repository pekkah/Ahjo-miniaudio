using Ahjo.Miniaudio.Internal;

namespace Ahjo.Miniaudio;

/// <summary>
/// Interleaved <c>f32</c> PCM in native memory: what <see cref="AudioDecoder.DecodeAll"/>
/// produces and <see cref="SoundAsset.FromPcm"/> plays. Invisible to the GC,
/// and never moves.
/// </summary>
public sealed unsafe class PcmBuffer : IDisposable
{
    private float* _samples;

    private PcmBuffer(float* samples, ulong frameCount, int channels, int sampleRate)
    {
        _samples = samples;
        FrameCount = frameCount;
        Channels = channels;
        SampleRate = sampleRate;
    }

    /// <summary>Allocates a zeroed (silent) buffer.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="channels"/> or <paramref name="sampleRate"/> is below 1, or the
    /// buffer would exceed <see cref="int.MaxValue"/> samples (what a <see cref="Span{T}"/> can address).
    /// </exception>
    public static PcmBuffer Allocate(ulong frameCount, int channels, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        var samples = SampleCount(frameCount, channels);
        return new PcmBuffer((float*)NativeBlock.Alloc((nuint)samples * sizeof(float)), frameCount, channels, sampleRate);
    }

    /// <summary>Takes ownership of <paramref name="samples"/>, which must come from <see cref="NativeBlock.Alloc(nuint)"/>.</summary>
    internal static PcmBuffer Adopt(float* samples, ulong frameCount, int channels, int sampleRate) =>
        new(samples, frameCount, channels, sampleRate);

    internal static int SampleCount(ulong frameCount, int channels)
    {
        var samples = frameCount * (ulong)channels;
        if (frameCount > int.MaxValue || samples > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount), frameCount,
                $"{frameCount} frames of {channels} channels exceeds {int.MaxValue} samples.");
        }

        return (int)samples;
    }

    /// <summary>Frames in the buffer; a frame is one sample per channel.</summary>
    public ulong FrameCount { get; }

    /// <summary>Samples per frame.</summary>
    public int Channels { get; }

    /// <summary>The rate the samples are meant to play at, in Hz.</summary>
    public int SampleRate { get; }

    /// <summary>The interleaved samples: <see cref="FrameCount"/> × <see cref="Channels"/> floats.</summary>
    public Span<float> Samples => new(Pointer, (int)FrameCount * Channels);

    internal float* Pointer
    {
        get
        {
            ObjectDisposedException.ThrowIf(_samples == null, this);
            return _samples;
        }
    }

    /// <summary>
    /// Moves the native samples out: the caller owns them and this buffer
    /// reads as disposed. How <see cref="SoundAsset.FromPcm"/> takes ownership
    /// without a copy and without the original handle being able to free them.
    /// </summary>
    internal float* Detach()
    {
        var samples = Pointer;
        _samples = null;
        return samples;
    }

    /// <summary>Frees the samples.</summary>
    public void Dispose()
    {
        if (_samples != null)
        {
            NativeBlock.Free(_samples);
            _samples = null;
        }
    }
}

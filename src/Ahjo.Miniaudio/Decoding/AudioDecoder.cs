using System.Runtime.InteropServices;

using Ahjo.Miniaudio.Internal;
using Ahjo.Miniaudio.Native;

namespace Ahjo.Miniaudio;

/// <summary>Output format for <see cref="AudioDecoder"/>. The default is valid: the file's own channels and rate.</summary>
public readonly record struct AudioDecoderDescription
{
    /// <summary>Output channels; 0 keeps the encoded channel count.</summary>
    public int Channels { get; init; }

    /// <summary>Output sample rate in Hz; 0 keeps the encoded rate. Anything else resamples while decoding.</summary>
    public int SampleRate { get; init; }

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(Channels, nameof(Channels));
        ArgumentOutOfRangeException.ThrowIfNegative(SampleRate, nameof(SampleRate));
    }
}

/// <summary>
/// Decodes WAV, FLAC or MP3 bytes (<c>ma_decoder</c>) to interleaved <c>f32</c>.
/// </summary>
/// <remarks>
/// <para><see cref="Create"/> copies the encoded bytes into native memory the
/// decoder owns (miniaudio reads them for the decoder's whole life), so the
/// caller's array is free to go as soon as it returns.</para>
/// <para>For a whole file at once, <see cref="DecodeAll"/>; for incremental
/// decoding, <see cref="Create"/> + <see cref="Read"/>.</para>
/// </remarks>
public sealed unsafe class AudioDecoder : IDisposable
{
    private byte* _data;
    private ma_decoder* _decoder;

    private AudioDecoder(byte* data, ma_decoder* decoder, int channels, int sampleRate)
    {
        _data = data;
        _decoder = decoder;
        Channels = channels;
        SampleRate = sampleRate;
    }

    /// <summary>Opens a decoder over a copy of <paramref name="encoded"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="encoded"/> is empty.</exception>
    /// <exception cref="MiniaudioException">The bytes are not a format miniaudio decodes.</exception>
    public static AudioDecoder Create(ReadOnlySpan<byte> encoded, in AudioDecoderDescription description = default)
    {
        description.Validate();
        var data = CopyToNative(encoded);
        try
        {
            var decoder = Open(data, (nuint)encoded.Length, description, out var channels, out var sampleRate);
            return new AudioDecoder(data, decoder, channels, sampleRate);
        }
        catch
        {
            FreeNative(data);
            throw;
        }
    }

    /// <summary>Decodes all of <paramref name="encoded"/> into a new <see cref="PcmBuffer"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="encoded"/> is empty.</exception>
    /// <exception cref="MiniaudioException">The bytes are not a format miniaudio decodes.</exception>
    public static PcmBuffer DecodeAll(ReadOnlySpan<byte> encoded, in AudioDecoderDescription description = default)
    {
        description.Validate();
        if (encoded.IsEmpty)
        {
            throw new ArgumentException("No encoded audio.", nameof(encoded));
        }

        // The span outlives this call's decoder, so no copy is needed here.
        fixed (byte* data = encoded)
        {
            var decoder = Open(data, (nuint)encoded.Length, description, out var channels, out var sampleRate);
            try
            {
                return ReadToEnd(decoder, channels, sampleRate);
            }
            finally
            {
                Close(decoder);
            }
        }
    }

    /// <summary>Output channels per frame.</summary>
    public int Channels { get; }

    /// <summary>Output sample rate in Hz.</summary>
    public int SampleRate { get; }

    /// <summary>
    /// Total length in output frames, or 0 when the format cannot report it
    /// without decoding.
    /// </summary>
    public ulong LengthInFrames => Length(Native);

    /// <summary>
    /// Decodes up to <c>output.Length / Channels</c> frames into
    /// <paramref name="output"/>. Returns the frames written; 0 at the end.
    /// Allocates nothing.
    /// </summary>
    public int Read(Span<float> output)
    {
        var decoder = Native;
        var frames = (ulong)(output.Length / Channels);
        return frames == 0 ? 0 : (int)ReadFrames(decoder, output, frames);
    }

    /// <summary>Moves the read position to output frame <paramref name="frame"/>.</summary>
    public void Seek(ulong frame) =>
        MaCheck.ThrowIfFailed(Ma.ma_decoder_seek_to_pcm_frame(Native, frame), "ma_decoder_seek_to_pcm_frame");

    private ma_decoder* Native
    {
        get
        {
            ObjectDisposedException.ThrowIf(_decoder == null, this);
            return _decoder;
        }
    }

    /// <summary>Closes the decoder and frees its copy of the encoded bytes.</summary>
    public void Dispose()
    {
        if (_decoder == null)
        {
            return;
        }

        Close(_decoder);
        FreeNative(_data);
        _decoder = null;
        _data = null;
    }

    internal static byte* CopyToNative(ReadOnlySpan<byte> encoded)
    {
        if (encoded.IsEmpty)
        {
            throw new ArgumentException("No encoded audio.", nameof(encoded));
        }

        var data = (byte*)NativeMemory.Alloc((nuint)encoded.Length);
        encoded.CopyTo(new Span<byte>(data, encoded.Length));
        return data;
    }

    internal static void FreeNative(byte* data) => NativeMemory.Free(data);

    /// <summary>
    /// Allocates and initializes an f32 memory decoder over <paramref name="data"/>
    /// (which must outlive it) and reads its output format. On any failure
    /// nothing is left allocated. Release with <see cref="Close"/>.
    /// </summary>
    internal static ma_decoder* Open(byte* data, nuint size, in AudioDecoderDescription description, out int channels, out int sampleRate)
    {
        var decoder = NativeBlock.Alloc<ma_decoder>();
        var config = Ma.ma_decoder_config_init(ma_format.ma_format_f32, (uint)description.Channels, (uint)description.SampleRate);
        var result = Ma.ma_decoder_init_memory(data, size, &config, decoder);
        if (result != ma_result.MA_SUCCESS)
        {
            NativeBlock.Free(decoder);
            throw new MiniaudioException(result, "ma_decoder_init_memory");
        }

        ma_format format;
        uint c, rate;
        result = Ma.ma_decoder_get_data_format(decoder, &format, &c, &rate, null, 0);
        if (result != ma_result.MA_SUCCESS)
        {
            Close(decoder);
            throw new MiniaudioException(result, "ma_decoder_get_data_format");
        }

        channels = (int)c;
        sampleRate = (int)rate;
        return decoder;
    }

    internal static void Close(ma_decoder* decoder)
    {
        Ma.ma_decoder_uninit(decoder);
        NativeBlock.Free(decoder);
    }

    /// <summary>Total length in output frames, or 0 when the format cannot report it without decoding.</summary>
    internal static ulong Length(ma_decoder* decoder)
    {
        ulong length;
        return Ma.ma_decoder_get_length_in_pcm_frames(decoder, &length) == ma_result.MA_SUCCESS ? length : 0;
    }

    private static PcmBuffer ReadToEnd(ma_decoder* decoder, int channels, int sampleRate)
    {
        // Size from the reported length when the format knows it (then one
        // read normally fills it exactly); otherwise start at one second.
        var length = Length(decoder);
        var capacity = length != 0 ? length : (ulong)sampleRate;
        var samples = (float*)NativeBlock.Alloc((nuint)PcmBuffer.SampleCount(capacity, channels) * sizeof(float));
        ulong total = 0;
        try
        {
            // One frame of lookahead: when the buffer is exactly full, probe
            // for more before growing, so an exactly-sized buffer stays exact.
            Span<float> probe = stackalloc float[channels];
            while (true)
            {
                if (total == capacity)
                {
                    if (ReadFrames(decoder, probe, 1) == 0)
                    {
                        break;
                    }

                    // Double, but never past what a PcmBuffer can address.
                    var limit = (ulong)(int.MaxValue / channels);
                    var grown = Math.Min(capacity * 2, limit);
                    if (grown == capacity)
                    {
                        throw new InvalidOperationException(
                            $"The decoded audio exceeds {int.MaxValue} samples, more than a PcmBuffer holds; stream it instead.");
                    }

                    samples = (float*)NativeBlock.Realloc(samples, (nuint)PcmBuffer.SampleCount(grown, channels) * sizeof(float));
                    capacity = grown;
                    probe.CopyTo(new Span<float>(samples + (total * (ulong)channels), channels));
                    total++;
                }

                var read = ReadFrames(decoder, new Span<float>(samples + (total * (ulong)channels), (int)((capacity - total) * (ulong)channels)), capacity - total);
                total += read;
                if (read == 0)
                {
                    break;
                }
            }

            // A guessed capacity (unknown length) is trimmed to what was decoded.
            if (total != capacity)
            {
                samples = (float*)NativeBlock.Realloc(samples, (nuint)PcmBuffer.SampleCount(total, channels) * sizeof(float));
            }
        }
        catch
        {
            NativeBlock.Free(samples);
            throw;
        }

        return PcmBuffer.Adopt(samples, total, channels, sampleRate);
    }

    // Reads up to `frames` frames; 0 at the end.
    private static ulong ReadFrames(ma_decoder* decoder, Span<float> destination, ulong frames)
    {
        ulong read;
        ma_result result;
        fixed (float* p = destination)
        {
            result = Ma.ma_decoder_read_pcm_frames(decoder, p, frames, &read);
        }

        if (result != ma_result.MA_AT_END)
        {
            MaCheck.ThrowIfFailed(result, "ma_decoder_read_pcm_frames");
        }

        return read;
    }
}

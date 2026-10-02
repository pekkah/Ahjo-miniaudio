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
        var decoder = NativeBlock.Alloc<ma_decoder>();
        var result = Init(data, (nuint)encoded.Length, description, decoder);
        if (result != ma_result.MA_SUCCESS)
        {
            NativeBlock.Free(decoder);
            FreeNative(data);
            throw new MiniaudioException(result, "ma_decoder_init_memory");
        }

        GetFormat(decoder, out var channels, out var sampleRate);
        return new AudioDecoder(data, decoder, channels, sampleRate);
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
        var decoder = NativeBlock.Alloc<ma_decoder>();
        try
        {
            fixed (byte* data = encoded)
            {
                MaCheck.ThrowIfFailed(Init(data, (nuint)encoded.Length, description, decoder), "ma_decoder_init_memory");
                try
                {
                    return ReadToEnd(decoder);
                }
                finally
                {
                    Ma.ma_decoder_uninit(decoder);
                }
            }
        }
        finally
        {
            NativeBlock.Free(decoder);
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
    public ulong LengthInFrames
    {
        get
        {
            ulong length;
            return Ma.ma_decoder_get_length_in_pcm_frames(Native, &length) == ma_result.MA_SUCCESS ? length : 0;
        }
    }

    /// <summary>
    /// Decodes up to <c>output.Length / Channels</c> frames into
    /// <paramref name="output"/>. Returns the frames written; 0 at the end.
    /// Allocates nothing.
    /// </summary>
    public int Read(Span<float> output)
    {
        var decoder = Native;
        var frames = (ulong)(output.Length / Channels);
        if (frames == 0)
        {
            return 0;
        }

        ulong read;
        ma_result result;
        fixed (float* p = output)
        {
            result = Ma.ma_decoder_read_pcm_frames(decoder, p, frames, &read);
        }

        if (result != ma_result.MA_AT_END)
        {
            MaCheck.ThrowIfFailed(result, "ma_decoder_read_pcm_frames");
        }

        return (int)read;
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

        Ma.ma_decoder_uninit(_decoder);
        NativeBlock.Free(_decoder);
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

    /// <summary>Initializes an f32 memory decoder. <paramref name="data"/> must outlive it.</summary>
    internal static ma_result Init(byte* data, nuint size, in AudioDecoderDescription description, ma_decoder* decoder)
    {
        var config = Ma.ma_decoder_config_init(ma_format.ma_format_f32, (uint)description.Channels, (uint)description.SampleRate);
        return Ma.ma_decoder_init_memory(data, size, &config, decoder);
    }

    internal static void GetFormat(ma_decoder* decoder, out int channels, out int sampleRate)
    {
        ma_format format;
        uint c, rate;
        MaCheck.ThrowIfFailed(Ma.ma_decoder_get_data_format(decoder, &format, &c, &rate, null, 0), "ma_decoder_get_data_format");
        channels = (int)c;
        sampleRate = (int)rate;
    }

    private static PcmBuffer ReadToEnd(ma_decoder* decoder)
    {
        GetFormat(decoder, out var channels, out var sampleRate);

        // Size from the reported length when the format knows it; otherwise
        // start at one second and double.
        ulong capacity;
        if (Ma.ma_decoder_get_length_in_pcm_frames(decoder, &capacity) != ma_result.MA_SUCCESS || capacity == 0)
        {
            capacity = (ulong)sampleRate;
        }

        var samples = (float*)NativeBlock.Alloc((nuint)PcmBuffer.SampleCount(capacity, channels) * sizeof(float));
        ulong total = 0;
        try
        {
            while (true)
            {
                if (total == capacity)
                {
                    var grown = capacity * 2;
                    var bytes = (nuint)PcmBuffer.SampleCount(grown, channels) * sizeof(float);
                    samples = (float*)NativeMemory.AlignedRealloc(samples, bytes, 64);
                    capacity = grown;
                }

                ulong read;
                var result = Ma.ma_decoder_read_pcm_frames(decoder, samples + (total * (ulong)channels), capacity - total, &read);
                total += read;
                if (result == ma_result.MA_AT_END || read == 0)
                {
                    break;
                }

                MaCheck.ThrowIfFailed(result, "ma_decoder_read_pcm_frames");
            }
        }
        catch
        {
            NativeBlock.Free(samples);
            throw;
        }

        return PcmBuffer.Adopt(samples, total, channels, sampleRate);
    }
}

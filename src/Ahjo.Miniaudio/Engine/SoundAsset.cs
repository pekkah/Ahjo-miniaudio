using Ahjo.Miniaudio.Internal;
using Ahjo.Miniaudio.Native;

namespace Ahjo.Miniaudio;

/// <summary>
/// Audio data that <see cref="Sound"/>s play: loaded once, shared by every
/// sound created from it. Independent of any engine.
/// </summary>
/// <remarks>
/// <para><b>Decoded</b> assets (<see cref="Decode"/>, <see cref="FromPcm"/>)
/// hold PCM; each sound gets its own cursor over the shared samples, so
/// instancing is cheap. Right for effects.</para>
/// <para><b>Streamed</b> assets (<see cref="Stream"/>) hold the encoded bytes;
/// each sound decodes them while it plays, on the audio thread. Right for
/// music and long ambience, where decoded PCM would be large.</para>
/// <para>Reference-counted: every sound keeps its asset's data alive, so
/// disposing an asset while sounds still play it is safe — the data is freed
/// when the last of them is disposed.</para>
/// </remarks>
public sealed unsafe class SoundAsset : IDisposable
{
    private float* _pcm;
    private byte* _encoded;
    private readonly nuint _encodedSize;
    private int _references = 1;
    private bool _disposed;

    private SoundAsset(float* pcm, byte* encoded, nuint encodedSize, int channels, int sampleRate, ulong lengthInFrames)
    {
        _pcm = pcm;
        _encoded = encoded;
        _encodedSize = encodedSize;
        Channels = channels;
        SampleRate = sampleRate;
        LengthInFrames = lengthInFrames;
    }

    /// <summary>Decodes all of <paramref name="encoded"/> (WAV, FLAC, MP3) to PCM now.</summary>
    /// <param name="encoded">The file's bytes; not retained.</param>
    /// <param name="sampleRate">
    /// Resample to this rate while decoding (pass the engine's, to spare the
    /// mixer a resampler per playing sound); 0 keeps the file's rate.
    /// </param>
    /// <exception cref="MiniaudioException">The bytes are not a format miniaudio decodes.</exception>
    public static SoundAsset Decode(ReadOnlySpan<byte> encoded, int sampleRate = 0) =>
        FromPcm(AudioDecoder.DecodeAll(encoded, new AudioDecoderDescription { SampleRate = sampleRate }));

    /// <summary>
    /// Keeps a copy of <paramref name="encoded"/> and decodes it while playing.
    /// The format is validated now.
    /// </summary>
    /// <param name="encoded">The file's bytes; copied, not retained.</param>
    /// <exception cref="MiniaudioException">The bytes are not a format miniaudio decodes.</exception>
    public static SoundAsset Stream(ReadOnlySpan<byte> encoded)
    {
        var data = AudioDecoder.CopyToNative(encoded);
        var size = (nuint)encoded.Length;
        try
        {
            var probe = AudioDecoder.Open(data, size, default, out var channels, out var sampleRate);
            try
            {
                return new SoundAsset(null, data, size, channels, sampleRate, AudioDecoder.Length(probe));
            }
            finally
            {
                AudioDecoder.Close(probe);
            }
        }
        catch
        {
            AudioDecoder.FreeNative(data);
            throw;
        }
    }

    /// <summary>
    /// Plays <paramref name="pcm"/> directly — cooked or generated audio. Takes
    /// ownership without copying: <paramref name="pcm"/> reads as disposed
    /// afterwards.
    /// </summary>
    public static SoundAsset FromPcm(PcmBuffer pcm)
    {
        ArgumentNullException.ThrowIfNull(pcm);
        var frames = pcm.FrameCount;
        var channels = pcm.Channels;
        var sampleRate = pcm.SampleRate;
        return new SoundAsset(pcm.Detach(), null, 0, channels, sampleRate, frames);
    }

    /// <summary>Whether sounds decode this asset while playing (<see cref="Stream"/>).</summary>
    public bool IsStreamed => _encodedSize != 0;

    /// <summary>Channels per frame.</summary>
    public int Channels { get; }

    /// <summary>The data's sample rate in Hz. Sounds resample to the engine's rate as they play.</summary>
    public int SampleRate { get; }

    /// <summary>Length in frames at <see cref="SampleRate"/>; 0 for a stream whose format cannot report it up front.</summary>
    public ulong LengthInFrames { get; }

    internal float* Pcm => _pcm;

    internal byte* Encoded => _encoded;

    internal nuint EncodedSize => _encodedSize;

    internal void AddReference()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Interlocked.Increment(ref _references);
    }

    internal void Release()
    {
        if (Interlocked.Decrement(ref _references) != 0)
        {
            return;
        }

        if (_pcm != null)
        {
            NativeBlock.Free(_pcm);
            _pcm = null;
        }

        if (_encoded != null)
        {
            AudioDecoder.FreeNative(_encoded);
            _encoded = null;
        }
    }

    /// <summary>
    /// Releases the asset. Sounds already created from it keep playing; the
    /// data is freed when the last of them is disposed.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Release();
    }
}

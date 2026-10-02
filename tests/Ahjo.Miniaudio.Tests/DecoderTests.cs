using Xunit;

namespace Ahjo.Miniaudio.Tests;

public class DecoderTests
{
    private static float Ramp(int frame, int channel) => ((frame % 200) - 100) / 200f + (channel * 0.1f);

    [Fact]
    public void DecoderReportsFormatAndDecodesSamples()
    {
        var wav = TestAudio.Wav(44100, 2, 1000, Ramp);
        using var decoder = AudioDecoder.Create(wav);

        Assert.Equal(2, decoder.Channels);
        Assert.Equal(44100, decoder.SampleRate);
        Assert.Equal(1000ul, decoder.LengthInFrames);

        var samples = new float[1000 * 2];
        Assert.Equal(1000, decoder.Read(samples));
        Assert.Equal(0, decoder.Read(samples)); // at end

        for (var frame = 0; frame < 1000; frame += 37)
        {
            Assert.Equal(Ramp(frame, 1), samples[(frame * 2) + 1], 0.001f);
        }
    }

    [Fact]
    public void DecoderCopiesItsInput()
    {
        var wav = TestAudio.ConstantWav(48000, 1, 100, 0.25f);
        using var decoder = AudioDecoder.Create(wav);
        Array.Clear(wav); // the decoder must not be reading the caller's array

        var samples = new float[100];
        Assert.Equal(100, decoder.Read(samples));
        Assert.All(samples, s => Assert.Equal(0.25f, s, 0.001f));
    }

    [Fact]
    public void SeekMovesTheReadPosition()
    {
        var wav = TestAudio.Wav(48000, 1, 1000, (frame, _) => frame / 1000f);
        using var decoder = AudioDecoder.Create(wav);

        decoder.Seek(500);
        Span<float> one = stackalloc float[1];
        Assert.Equal(1, decoder.Read(one));
        Assert.Equal(0.5f, one[0], 0.001f);
    }

    [Fact]
    public void DecodeAllReadsTheWholeFile()
    {
        var wav = TestAudio.Wav(22050, 2, 3000, Ramp);
        using var pcm = AudioDecoder.DecodeAll(wav);

        Assert.Equal(3000ul, pcm.FrameCount);
        Assert.Equal(2, pcm.Channels);
        Assert.Equal(22050, pcm.SampleRate);
        Assert.Equal(Ramp(1234, 0), pcm.Samples[1234 * 2], 0.001f);
    }

    [Fact]
    public void DecodeAllResamplesToTheRequestedRate()
    {
        var wav = TestAudio.ConstantWav(44100, 1, 4410, 0.5f);
        using var pcm = AudioDecoder.DecodeAll(wav, new AudioDecoderDescription { SampleRate = 48000 });

        Assert.Equal(48000, pcm.SampleRate);
        Assert.InRange(pcm.FrameCount, 4790ul, 4810ul); // 0.1 s at 48 kHz
    }

    [Fact]
    public void GarbageIsAMiniaudioException()
    {
        var garbage = new byte[256];
        new Random(1).NextBytes(garbage);

        var e = Assert.Throws<MiniaudioException>(() => AudioDecoder.Create(garbage));
        Assert.Equal("ma_decoder_init_memory", e.Operation);
        Assert.Throws<MiniaudioException>(() => AudioDecoder.DecodeAll(garbage));
        Assert.Throws<MiniaudioException>(() => SoundAsset.Stream(garbage));
    }

    [Fact]
    public void EmptyInputIsAnArgumentException()
    {
        Assert.Throws<ArgumentException>(() => AudioDecoder.Create([]));
        Assert.Throws<ArgumentException>(() => AudioDecoder.DecodeAll([]));
    }

    [Fact]
    public void PcmBufferStartsSilentAndRejectsBadShapes()
    {
        using var pcm = PcmBuffer.Allocate(10, 2, 48000);
        Assert.Equal(20, pcm.Samples.Length);
        Assert.All(pcm.Samples.ToArray(), s => Assert.Equal(0f, s));

        Assert.Throws<ArgumentOutOfRangeException>(() => PcmBuffer.Allocate(10, 0, 48000));
        Assert.Throws<ArgumentOutOfRangeException>(() => PcmBuffer.Allocate((ulong)int.MaxValue, 2, 48000));
    }

    [Fact]
    public void FromPcmTakesOwnership()
    {
        var pcm = PcmBuffer.Allocate(10, 1, 48000);
        using var asset = SoundAsset.FromPcm(pcm);

        Assert.Equal(10ul, asset.LengthInFrames);
        Assert.Throws<ObjectDisposedException>(() => pcm.Samples.Length);
        pcm.Dispose(); // harmless: the asset owns the samples now
    }
}

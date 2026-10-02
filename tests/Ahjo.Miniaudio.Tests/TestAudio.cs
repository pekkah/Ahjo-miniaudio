using System.Buffers.Binary;
using System.Diagnostics;

namespace Ahjo.Miniaudio.Tests;

/// <summary>
/// Audio fixtures built in memory, so the tests need no files: 16-bit PCM WAV
/// bytes (the decoder's input), PCM assets, and a pull-mode engine whose mix
/// is read synchronously — deterministic, no device, no timing.
/// </summary>
internal static class TestAudio
{
    public const int EngineRate = 48000;

    /// <summary>A 16-bit PCM WAV whose sample at (frame, channel) is <paramref name="sample"/>.</summary>
    public static byte[] Wav(int sampleRate, int channels, int frames, Func<int, int, float> sample)
    {
        const int headerSize = 44;
        var dataSize = frames * channels * sizeof(short);
        var wav = new byte[headerSize + dataSize];
        var span = wav.AsSpan();

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + dataSize);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], sampleRate * channels * sizeof(short));
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], (short)(channels * sizeof(short)));
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataSize);

        var offset = headerSize;
        for (var frame = 0; frame < frames; frame++)
        {
            for (var channel = 0; channel < channels; channel++)
            {
                var value = Math.Clamp(sample(frame, channel), -1f, 1f);
                BinaryPrimitives.WriteInt16LittleEndian(span[offset..], (short)Math.Round(value * short.MaxValue));
                offset += sizeof(short);
            }
        }

        return wav;
    }

    public static byte[] ConstantWav(int sampleRate, int channels, int frames, float value) =>
        Wav(sampleRate, channels, frames, (_, _) => value);

    /// <summary>A mono asset holding <paramref name="value"/> for <paramref name="frames"/> frames.</summary>
    public static SoundAsset ConstantAsset(int frames, float value = 0.5f, int sampleRate = EngineRate)
    {
        var pcm = PcmBuffer.Allocate((ulong)frames, 1, sampleRate);
        pcm.Samples.Fill(value);
        return SoundAsset.FromPcm(pcm);
    }

    public static AudioEngine PullEngine(int channels = 2, int sampleRate = EngineRate) =>
        AudioEngine.Create(new AudioEngineDescription { NoDevice = true, Channels = channels, SampleRate = sampleRate });

    /// <summary>Mixes <paramref name="frames"/> frames and returns the peak absolute sample.</summary>
    public static float ReadPeak(AudioEngine engine, int frames)
    {
        var buffer = new float[frames * engine.Channels];
        engine.Read(buffer);
        return Peak(buffer);
    }

    public static float Peak(ReadOnlySpan<float> samples)
    {
        var peak = 0f;
        foreach (var s in samples)
        {
            peak = Math.Max(peak, Math.Abs(s));
        }

        return peak;
    }

    /// <summary>Mixes in <paramref name="chunk"/>-frame steps until <paramref name="sound"/> stops; returns the frames mixed.</summary>
    public static ulong FramesUntilStopped(AudioEngine engine, Sound sound, int chunk = 64, ulong limit = 10 * EngineRate)
    {
        var buffer = new float[chunk * engine.Channels];
        ulong mixed = 0;
        while (sound.IsPlaying && mixed < limit)
        {
            engine.Read(buffer);
            mixed += (ulong)chunk;
        }

        return mixed;
    }

    /// <summary>Spins until <paramref name="condition"/> holds or five seconds pass (null-backend devices run in real time).</summary>
    public static bool WaitUntil(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(5))
            {
                return false;
            }

            Thread.Sleep(5);
        }

        return true;
    }

    public static AudioContext NullContext() =>
        AudioContext.Create(new AudioContextDescription { Backend = AudioBackend.Null });
}

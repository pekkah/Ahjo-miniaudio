using Xunit;

namespace Ahjo.Miniaudio.Tests;

/// <summary>Pins the fixes from the PR review of issue #2's wrapper.</summary>
public class ReviewRegressionTests
{
    [Fact]
    public void DisposingAPulledEngineUnderARunningDeviceIsSafe()
    {
        using var context = TestAudio.NullContext();
        var engine = TestAudio.PullEngine();
        var asset = TestAudio.ConstantAsset(480);
        _ = Sound.Create(engine, asset, new SoundDescription { Looping = true });
        asset.Dispose();

        var renderer = new PullRenderer(engine);
        using var device = AudioDevice.Create(context, new AudioDeviceDescription { Channels = 2, SampleRate = 48000 }, renderer, TestContext.Current.CancellationToken);
        device.Start(TestContext.Current.CancellationToken);
        Assert.True(TestAudio.WaitUntil(() => Volatile.Read(ref renderer.Reads) > 10));

        // The wrong order on purpose: the engine goes while the audio thread
        // is pulling it. Dispose waits out a read in flight; later reads
        // throw, and the device latches that and plays silence.
        engine.Dispose();

        Assert.True(TestAudio.WaitUntil(() => device.Fault is not null), "the read after dispose was not reported");
        Assert.IsType<ObjectDisposedException>(device.Fault);
        Assert.True(device.IsStarted);
    }

    [Fact]
    public void NoDeviceEngineStartAndStopAreCallerErrors()
    {
        using var engine = TestAudio.PullEngine();

        Assert.Throws<InvalidOperationException>(() => engine.Start(TestContext.Current.CancellationToken));
        Assert.Throws<InvalidOperationException>(() => engine.Stop(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void GroupsCannotCrossEngines()
    {
        using var a = TestAudio.PullEngine();
        using var b = TestAudio.PullEngine();
        using var asset = TestAudio.ConstantAsset(480);
        using var groupOnB = SoundGroup.Create(b);

        Assert.Throws<ArgumentException>(() => Sound.Create(a, asset, new SoundDescription { Group = groupOnB }));
        Assert.Throws<ArgumentException>(() => SoundGroup.Create(a, groupOnB));

        // The rejected create took no asset reference.
        asset.Dispose();
        Assert.Throws<ObjectDisposedException>(() => Sound.Create(b, asset));
    }

    [Fact]
    public void GroupOnADisposedParentOrEngineIsRejected()
    {
        var engine = TestAudio.PullEngine();
        var parent = SoundGroup.Create(engine);
        parent.Dispose();

        Assert.Throws<ObjectDisposedException>(() => SoundGroup.Create(engine, parent));
        engine.Dispose();
        Assert.Throws<ObjectDisposedException>(() => SoundGroup.Create(engine));
    }

    [Fact]
    public void PoolStealsTheLeastRecentlyPlayedEvenAfterAnOutOfOrderReuse()
    {
        using var engine = TestAudio.PullEngine();
        using var asset = TestAudio.ConstantAsset(TestAudio.EngineRate);
        using var pool = SoundPool.Create(engine, asset, 4);

        var v0 = pool.Play();
        var v1 = pool.Play();
        var v2 = pool.Play();
        var v3 = pool.Play();
        v1.Stop();

        Assert.Same(v1, pool.Play()); // the only idle voice
        Assert.Same(v0, pool.Play()); // all busy: v0 was played longest ago
        Assert.Same(v2, pool.Play());
        Assert.Same(v3, pool.Play());
        Assert.Same(v1, pool.Play());
    }

    [Fact]
    public void DecodeAllOfAnExactLengthFileKeepsEveryFrame()
    {
        // Two seconds: well past the first read, exercising the
        // full-buffer probe without growing an exactly sized buffer.
        var wav = TestAudio.Wav(48000, 2, 96000, (frame, channel) => ((frame % 100) / 100f) - (channel * 0.25f));
        using var pcm = AudioDecoder.DecodeAll(wav);

        Assert.Equal(96000ul, pcm.FrameCount);
        Assert.Equal(0.99f, pcm.Samples[(95999 * 2)], 0.001f);
        Assert.Equal(0.99f - 0.25f, pcm.Samples[(95999 * 2) + 1], 0.001f);
    }

    private sealed class PullRenderer(AudioEngine engine) : IAudioRenderer
    {
        public int Reads;

        public void Render(Span<float> output, int channels)
        {
            engine.Read(output);
            Interlocked.Increment(ref Reads);
        }
    }
}

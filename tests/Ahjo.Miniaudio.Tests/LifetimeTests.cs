using System.Numerics;

using Xunit;

namespace Ahjo.Miniaudio.Tests;

/// <summary>
/// Disposal order is miniaudio's hardest rule (children before parents, data
/// after the sounds reading it). The wrapper owns that order; these tests do
/// it wrong on purpose and expect no crash.
/// </summary>
public class LifetimeTests
{
    [Fact]
    public void DisposingTheEngineDisposesItsSoundsAndGroups()
    {
        using var asset = TestAudio.ConstantAsset(480);
        var engine = TestAudio.PullEngine();
        var group = SoundGroup.Create(engine);
        var sound = Sound.Create(engine, asset, new SoundDescription { Group = group });
        sound.Play();

        engine.Dispose();

        Assert.Throws<ObjectDisposedException>(() => sound.IsPlaying);
        Assert.Throws<ObjectDisposedException>(() => group.Volume);
        Assert.Throws<ObjectDisposedException>(() => engine.TimeInFrames);
        Assert.Throws<ObjectDisposedException>(() => Sound.Create(engine, asset));
        sound.Dispose();
        group.Dispose();
        engine.Dispose();
    }

    [Fact]
    public void DisposingAGroupFirstLeavesItsSoundsSafe()
    {
        using var engine = TestAudio.PullEngine();
        using var asset = TestAudio.ConstantAsset(TestAudio.EngineRate);
        var group = SoundGroup.Create(engine);
        using var sound = Sound.Create(engine, asset, new SoundDescription { Group = group });
        sound.Play();

        group.Dispose();

        // Detached from the graph: silent, but alive and controllable.
        Assert.Equal(0f, TestAudio.ReadPeak(engine, 480));
        sound.Volume = 0.5f;
        Assert.True(sound.IsPlaying);
    }

    [Fact]
    public void DisposingAnAssetWhileItPlaysKeepsItsDataAlive()
    {
        using var engine = TestAudio.PullEngine();
        var asset = TestAudio.ConstantAsset(TestAudio.EngineRate);
        var sound = Sound.Create(engine, asset);
        sound.Play();

        asset.Dispose();

        Assert.InRange(TestAudio.ReadPeak(engine, 480), 0.4f, 0.6f);
        Assert.Throws<ObjectDisposedException>(() => Sound.Create(engine, asset));
        sound.Dispose(); // the last reference: frees the PCM
        asset.Dispose();
    }

    [Fact]
    public void StreamedAssetOutlivesItsOwnerToo()
    {
        using var engine = TestAudio.PullEngine();
        var asset = SoundAsset.Stream(TestAudio.ConstantWav(48000, 1, 48000, 0.5f));
        using var sound = Sound.Create(engine, asset);
        sound.Play();

        asset.Dispose();

        Assert.InRange(TestAudio.ReadPeak(engine, 4800), 0.4f, 0.6f);
    }

    [Fact]
    public void DoubleDisposeIsHarmlessEverywhere()
    {
        var context = TestAudio.NullContext();
        var engine = TestAudio.PullEngine();
        var asset = TestAudio.ConstantAsset(480);
        var group = SoundGroup.Create(engine);
        var sound = Sound.Create(engine, asset);
        var pool = SoundPool.Create(engine, asset, 2);
        var decoder = AudioDecoder.Create(TestAudio.ConstantWav(48000, 1, 10, 0));
        var pcm = PcmBuffer.Allocate(1, 1, 48000);

        IDisposable[] all = [pool, sound, group, asset, engine, decoder, pcm, context];
        foreach (var d in all)
        {
            d.Dispose();
            d.Dispose();
        }
    }

    [Fact]
    public void FailedSoundCreationReleasesTheAsset()
    {
        using var engine = TestAudio.PullEngine();
        var asset = TestAudio.ConstantAsset(480);
        var group = SoundGroup.Create(engine);
        group.Dispose();

        Assert.Throws<ObjectDisposedException>(() => Sound.Create(engine, asset, new SoundDescription { Group = group }));

        // Had the failed create kept a reference, this sound's dispose would
        // not be the last one; either way nothing may crash.
        var sound = Sound.Create(engine, asset);
        asset.Dispose();
        sound.Dispose();
    }

    [Fact]
    public void PoolVoicesAreDisposedWithTheEngine()
    {
        using var asset = TestAudio.ConstantAsset(480);
        var engine = TestAudio.PullEngine();
        using var pool = SoundPool.Create(engine, asset, 3, new SoundDescription { Spatialized = true });

        engine.Dispose();

        Assert.Throws<ObjectDisposedException>(() => pool.Play(Vector3.Zero));
    }
}

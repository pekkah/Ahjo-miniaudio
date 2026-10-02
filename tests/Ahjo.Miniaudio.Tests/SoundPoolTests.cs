using System.Numerics;

using Xunit;

namespace Ahjo.Miniaudio.Tests;

public class SoundPoolTests
{
    [Fact]
    public void PlayUsesIdleVoicesThenStealsTheLeastRecentlyPlayed()
    {
        using var engine = TestAudio.PullEngine();
        using var asset = TestAudio.ConstantAsset(TestAudio.EngineRate);
        using var pool = SoundPool.Create(engine, asset, 3);

        var a = pool.Play();
        var b = pool.Play();
        var c = pool.Play();

        Assert.Equal(3, new HashSet<Sound> { a, b, c }.Count);
        Assert.True(a.IsPlaying && b.IsPlaying && c.IsPlaying);

        Assert.Same(a, pool.Play()); // all busy: a is the oldest
        Assert.Same(b, pool.Play());
    }

    [Fact]
    public void PlayPrefersAVoiceThatHasFinished()
    {
        using var engine = TestAudio.PullEngine();
        using var shortAsset = TestAudio.ConstantAsset(480);
        using var pool = SoundPool.Create(engine, shortAsset, 2);

        var first = pool.Play();
        TestAudio.FramesUntilStopped(engine, first);
        var second = pool.Play(); // the next voice in turn, idle anyway

        Assert.NotSame(first, second);
        Assert.Same(first, pool.Play()); // finished, so it is free again
    }

    [Fact]
    public void PlayAtPositionPlacesTheVoice()
    {
        using var engine = TestAudio.PullEngine();
        using var asset = TestAudio.ConstantAsset(480);
        using var pool = SoundPool.Create(engine, asset, 2, new SoundDescription { Spatialized = true });

        var voice = pool.Play(new Vector3(1, 2, 3));

        Assert.Equal(new Vector3(1, 2, 3), voice.Position);
        Assert.Equal(2, pool.Voices.Length);

        pool.Stop();
        Assert.False(voice.IsPlaying);
    }

    [Fact]
    public void PoolNeedsAtLeastOneVoice()
    {
        using var engine = TestAudio.PullEngine();
        using var asset = TestAudio.ConstantAsset(480);

        Assert.Throws<ArgumentOutOfRangeException>(() => SoundPool.Create(engine, asset, 0));
    }
}

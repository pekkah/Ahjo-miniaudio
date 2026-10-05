using System.Numerics;

using Xunit;

namespace Ahjo.Miniaudio.Tests;

/// <summary>
/// CLAUDE.md invariant 5: nothing a game frame calls allocates. Measured on
/// this thread across a loop of every per-frame member, after a warm-up pass.
/// The device render path has its own probe in <see cref="DeviceTests.RenderPathAllocatesNothing"/>.
/// </summary>
public class AllocationTests
{
    [Fact]
    public void PerFrameControlAllocatesNothing()
    {
        using var engine = TestAudio.PullEngine();
        using var asset = TestAudio.ConstantAsset(4800);
        using var group = SoundGroup.Create(engine);
        using var sound = Sound.Create(engine, asset, new SoundDescription { Group = group, Spatialized = true });
        using var pool = SoundPool.Create(engine, asset, 4, new SoundDescription { Spatialized = true });
        var mix = new float[256 * engine.Channels];
        var pose = Matrix4x4.CreateWorld(new Vector3(1, 2, 3), -Vector3.UnitZ, Vector3.UnitY);

        // A full warm-up pass reaches every branch once (JIT, static
        // initialization), so only steady state is measured.
        for (var i = 0; i < 100; i++)
        {
            Frame(engine, sound, group, pool, mix, pose, i);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            Frame(engine, sound, group, pool, mix, pose, i);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void StartAndStopWithoutATokenAllocateNothing()
    {
        // The token path runs on its own thread; the plain path must stay a
        // direct native call. CancellationToken.None is that plain path.
        using var context = TestAudio.NullContext();
        using var device = AudioDevice.Create(context, default, new SilentRenderer(), CancellationToken.None);
        using var engine = AudioEngine.Create(new AudioEngineDescription { Context = context, NoAutoStart = true }, CancellationToken.None);
        StartStop(device, engine);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10; i++)
        {
            StartStop(device, engine);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static void StartStop(AudioDevice device, AudioEngine engine)
    {
        device.Start(CancellationToken.None);
        device.Stop(CancellationToken.None);
        engine.Start(CancellationToken.None);
        engine.Stop(CancellationToken.None);
    }

    private sealed class SilentRenderer : IAudioRenderer
    {
        public void Render(Span<float> output, int channels) => output.Clear();
    }

    private static void Frame(AudioEngine engine, Sound sound, SoundGroup group, SoundPool pool, float[] mix, in Matrix4x4 pose, int i)
    {
        var t = i / 100f;

        var listener = engine.Listener;
        listener.SetPose(pose);
        listener.Velocity = new Vector3(t, 0, 0);

        if (i % 10 == 0)
        {
            sound.Play();
            pool.Play(new Vector3(t, 0, -1));
            pool.Play();
        }

        sound.Volume = 1 - t;
        sound.Pan = t;
        sound.Pitch = 1 + t;
        sound.Position = new Vector3(0, 0, -t);
        sound.Velocity = Vector3.UnitX;
        sound.Looping = i % 2 == 0;
        group.Volume = 1 - t;
        _ = sound.IsPlaying;
        _ = sound.AtEnd;
        _ = sound.CursorInFrames;
        _ = engine.TimeInFrames;

        if (i == 50)
        {
            sound.StopWithFade(TimeSpan.FromMilliseconds(5));
        }

        engine.Read(mix);
    }
}

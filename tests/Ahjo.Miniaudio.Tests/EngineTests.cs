using System.Numerics;

using Xunit;

namespace Ahjo.Miniaudio.Tests;

/// <summary>
/// The engine tier, mixed synchronously through a NoDevice engine: every
/// frame of output is the test's own <see cref="AudioEngine.Read"/>, so these
/// are deterministic.
/// </summary>
public class EngineTests
{
    [Fact]
    public void NoDeviceEngineDefaultsToStereo48k()
    {
        using var engine = AudioEngine.Create(new AudioEngineDescription { NoDevice = true });

        Assert.Equal(2, engine.Channels);
        Assert.Equal(48000, engine.SampleRate);
        Assert.Equal(1, engine.ListenerCount);
    }

    [Fact]
    public void EngineOnANullDeviceRunsItsClock()
    {
        using var context = TestAudio.NullContext();
        using var engine = AudioEngine.Create(new AudioEngineDescription { Context = context });
        using var asset = TestAudio.ConstantAsset(480);
        using var sound = Sound.Create(engine, asset, new SoundDescription { Looping = true });
        sound.Play();

        Assert.True(TestAudio.WaitUntil(() => engine.TimeInFrames > 0), "the engine's device never ran");
        Assert.Throws<InvalidOperationException>(() => engine.Read(new float[engine.Channels]));
    }

    [Fact]
    public void ReadAdvancesTheClockAndRejectsPartialFrames()
    {
        using var engine = TestAudio.PullEngine();

        // miniaudio's clock is the output node's: with nothing attached to
        // it, a read mixes nothing and the clock stands still.
        engine.Read(new float[480 * 2]);
        Assert.Equal(0ul, engine.TimeInFrames);

        using var asset = TestAudio.ConstantAsset(480);
        using var sound = Sound.Create(engine, asset); // attached, not even playing
        engine.Read(new float[480 * 2]);
        Assert.Equal(480ul, engine.TimeInFrames);
        Assert.Throws<ArgumentException>(() => engine.Read(new float[3]));
    }

    [Fact]
    public void PlayingSoundIsAudibleAndStoppedSoundIsNot()
    {
        using var engine = TestAudio.PullEngine();
        using var asset = TestAudio.ConstantAsset(TestAudio.EngineRate);
        using var sound = Sound.Create(engine, asset);

        Assert.False(sound.IsPlaying);
        Assert.Equal(0f, TestAudio.ReadPeak(engine, 480));

        sound.Play();
        Assert.True(sound.IsPlaying);
        Assert.InRange(TestAudio.ReadPeak(engine, 480), 0.4f, 0.6f);

        sound.Stop();
        Assert.Equal(0f, TestAudio.ReadPeak(engine, 480));
    }

    [Fact]
    public void VolumeScalesTheMix()
    {
        using var engine = TestAudio.PullEngine();
        using var asset = TestAudio.ConstantAsset(TestAudio.EngineRate);
        using var sound = Sound.Create(engine, asset);
        sound.Play();

        sound.Volume = 0.5f;
        Assert.Equal(0.5f, sound.Volume);
        Assert.InRange(TestAudio.ReadPeak(engine, 480), 0.2f, 0.3f);

        engine.Volume = 0;
        Assert.Equal(0f, TestAudio.ReadPeak(engine, 480), 0.0001f);
    }

    [Fact]
    public void SoundEndsAfterItsLengthAtTheAssetRate()
    {
        // 0.1 s of 24 kHz audio on a 48 kHz engine is 4800 mixed frames. If
        // the asset's rate were lost (ma_audio_buffer_ref reports 0, which
        // ma_sound reads as the engine's rate) it would end after 2400.
        using var engine = TestAudio.PullEngine();
        using var asset = TestAudio.ConstantAsset(2400, sampleRate: 24000);
        using var sound = Sound.Create(engine, asset);
        sound.Play();

        var frames = TestAudio.FramesUntilStopped(engine, sound);

        // Resampler latency and the 64-frame read chunk add a little.
        Assert.InRange(frames, 4700ul, 5100ul);
        Assert.True(sound.AtEnd);
    }

    [Fact]
    public void PlayAfterStopWithFadeIsAudibleAgain()
    {
        using var engine = TestAudio.PullEngine();
        using var asset = TestAudio.ConstantAsset(TestAudio.EngineRate);
        using var sound = Sound.Create(engine, asset);

        sound.Play();
        sound.StopWithFade(TimeSpan.FromMilliseconds(10));
        TestAudio.FramesUntilStopped(engine, sound);
        Assert.False(sound.IsPlaying);
        Assert.Equal(0f, TestAudio.ReadPeak(engine, 480));

        sound.Play();

        Assert.InRange(TestAudio.ReadPeak(engine, 480), 0.4f, 0.6f);
    }

    [Fact]
    public void LoopingSoundNeverEnds()
    {
        using var engine = TestAudio.PullEngine();
        using var asset = TestAudio.ConstantAsset(480);
        using var sound = Sound.Create(engine, asset, new SoundDescription { Looping = true });
        Assert.True(sound.Looping);

        sound.Play();
        var frames = TestAudio.FramesUntilStopped(engine, sound, limit: 48000);

        Assert.Equal(48000ul, frames);
        Assert.True(sound.IsPlaying);
        Assert.InRange(TestAudio.ReadPeak(engine, 480), 0.4f, 0.6f);
    }

    [Fact]
    public void SeekAndCursorAreInAssetFrames()
    {
        using var engine = TestAudio.PullEngine();
        using var asset = TestAudio.ConstantAsset(48000);
        using var sound = Sound.Create(engine, asset);

        Assert.Equal(48000ul, sound.LengthInFrames);
        sound.Seek(1000);
        Assert.Equal(1000ul, sound.CursorInFrames);
    }

    [Fact]
    public void GroupVolumeAppliesToItsSounds()
    {
        using var engine = TestAudio.PullEngine();
        using var asset = TestAudio.ConstantAsset(TestAudio.EngineRate);
        using var music = SoundGroup.Create(engine);
        using var sound = Sound.Create(engine, asset, new SoundDescription { Group = music });
        sound.Play();

        Assert.True(music.IsPlaying);
        Assert.InRange(TestAudio.ReadPeak(engine, 480), 0.4f, 0.6f);

        music.Volume = 0;
        Assert.Equal(0f, TestAudio.ReadPeak(engine, 480), 0.0001f);

        music.Volume = 1;
        music.Stop();
        Assert.Equal(0f, TestAudio.ReadPeak(engine, 480));
    }

    [Fact]
    public void NestedGroupsMix()
    {
        using var engine = TestAudio.PullEngine();
        using var asset = TestAudio.ConstantAsset(TestAudio.EngineRate);
        using var master = SoundGroup.Create(engine);
        using var effects = SoundGroup.Create(engine, master);
        using var sound = Sound.Create(engine, asset, new SoundDescription { Group = effects });
        sound.Play();

        master.Volume = 0.5f;

        Assert.InRange(TestAudio.ReadPeak(engine, 480), 0.2f, 0.3f);
    }

    [Fact]
    public void StreamedAssetPlaysToItsEnd()
    {
        var wav = TestAudio.ConstantWav(48000, 1, 4800, 0.5f);
        using var engine = TestAudio.PullEngine();
        using var asset = SoundAsset.Stream(wav);
        Array.Clear(wav);

        Assert.True(asset.IsStreamed);
        Assert.Equal(4800ul, asset.LengthInFrames);

        using var sound = Sound.Create(engine, asset);
        sound.Play();
        Assert.InRange(TestAudio.ReadPeak(engine, 480), 0.4f, 0.6f);
        Assert.InRange(TestAudio.FramesUntilStopped(engine, sound), 4200ul, 4500ul); // 4800 less the 480 above
    }

    [Fact]
    public void DecodedAssetResamplesAtLoad()
    {
        var wav = TestAudio.ConstantWav(44100, 2, 4410, 0.5f);
        using var asset = SoundAsset.Decode(wav, sampleRate: 48000);

        Assert.False(asset.IsStreamed);
        Assert.Equal(2, asset.Channels);
        Assert.Equal(48000, asset.SampleRate);
        Assert.InRange(asset.LengthInFrames, 4790ul, 4810ul);
    }

    [Fact]
    public void SpatializedSoundAttenuatesWithDistance()
    {
        using var engine = TestAudio.PullEngine();
        using var asset = TestAudio.ConstantAsset(TestAudio.EngineRate);
        using var sound = Sound.Create(engine, asset, new SoundDescription { Spatialized = true, Looping = true });
        Assert.True(sound.Spatialized);
        sound.AttenuationModel = AttenuationModel.Inverse;
        sound.Play();

        sound.Position = new Vector3(0, 0, -1);
        TestAudio.ReadPeak(engine, 4800); // let gain smoothing settle
        var near = TestAudio.ReadPeak(engine, 480);

        sound.Position = new Vector3(0, 0, -50);
        TestAudio.ReadPeak(engine, 4800);
        var far = TestAudio.ReadPeak(engine, 480);

        Assert.True(near > far * 5, $"near {near}, far {far}");
    }

    [Fact]
    public void SoundPropertiesRoundTrip()
    {
        using var engine = TestAudio.PullEngine();
        using var asset = TestAudio.ConstantAsset(480);
        using var sound = Sound.Create(engine, asset);

        sound.Pan = -0.5f;
        sound.Pitch = 1.5f;
        sound.Position = new Vector3(1, 2, 3);
        sound.Velocity = new Vector3(4, 5, 6);
        sound.Direction = new Vector3(0, 0, 1);
        sound.Positioning = Positioning.Relative;
        sound.AttenuationModel = AttenuationModel.Linear;
        sound.Rolloff = 2;
        sound.MinDistance = 3;
        sound.MaxDistance = 30;
        sound.DopplerFactor = 0;

        Assert.Equal(-0.5f, sound.Pan);
        Assert.Equal(1.5f, sound.Pitch);
        Assert.Equal(new Vector3(1, 2, 3), sound.Position);
        Assert.Equal(new Vector3(4, 5, 6), sound.Velocity);
        Assert.Equal(new Vector3(0, 0, 1), sound.Direction);
        Assert.Equal(Positioning.Relative, sound.Positioning);
        Assert.Equal(AttenuationModel.Linear, sound.AttenuationModel);
        Assert.Equal(2, sound.Rolloff);
        Assert.Equal(3, sound.MinDistance);
        Assert.Equal(30, sound.MaxDistance);
        Assert.Equal(0, sound.DopplerFactor);
        Assert.Same(asset, sound.Asset);
    }

    [Fact]
    public void ListenerSetPoseReadsANumericsWorldMatrix()
    {
        using var engine = TestAudio.PullEngine();
        var listener = engine.Listener;
        var position = new Vector3(10, 2, -3);
        var forward = Vector3.Normalize(new Vector3(1, 0, -1));

        // CreateWorld puts forward on −Z (its third row is −forward), the
        // camera convention SetPose expects; the scale must not leak through.
        listener.SetPose(Matrix4x4.CreateScale(3) * Matrix4x4.CreateWorld(position, forward, Vector3.UnitY));

        Assert.Equal(position, listener.Position);
        AssertNear(forward, listener.Direction);
        AssertNear(Vector3.UnitY, listener.WorldUp);
    }

    [Fact]
    public void ListenersAreIndexedAndValidated()
    {
        using var engine = AudioEngine.Create(new AudioEngineDescription { NoDevice = true, ListenerCount = 2 });

        var second = engine.GetListener(1);
        second.Velocity = new Vector3(1, 0, 0);
        second.Enabled = false;

        Assert.Equal(1, second.Index);
        Assert.Equal(new Vector3(1, 0, 0), second.Velocity);
        Assert.False(second.Enabled);
        Assert.True(engine.Listener.Enabled);
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.GetListener(2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AudioEngine.Create(new AudioEngineDescription { NoDevice = true, ListenerCount = 5 }));
        Assert.Throws<InvalidOperationException>(() => default(AudioListener).Position);
    }

    private static void AssertNear(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-5f, $"expected {expected}, got {actual}");
}

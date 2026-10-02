using System.Collections.Concurrent;

using Xunit;

namespace Ahjo.Miniaudio.Tests;

/// <summary>The device tier on the null backend: a real audio thread, no hardware.</summary>
public class DeviceTests
{
    [Fact]
    public void NullContextReportsItsBackendAndAPlaybackDevice()
    {
        using var context = TestAudio.NullContext();

        Assert.Equal(AudioBackend.Null, context.Backend);
        var devices = context.GetPlaybackDevices();
        Assert.NotEmpty(devices);
        Assert.False(string.IsNullOrEmpty(devices[0].Name));

        // Ids are stable across enumerations and compare by value.
        Assert.Equal(devices[0].Id, context.GetPlaybackDevices()[0].Id);
    }

    [Fact]
    public void RendererIsCalledWithTheDeviceChannelCount()
    {
        using var context = TestAudio.NullContext();
        var renderer = new CountingRenderer();
        using var device = AudioDevice.Create(context, new AudioDeviceDescription { Channels = 2, SampleRate = 48000 }, renderer);

        Assert.Equal(2, device.Channels);
        Assert.Equal(48000, device.SampleRate);
        Assert.False(device.IsStarted);

        device.Start();
        Assert.True(TestAudio.WaitUntil(() => Volatile.Read(ref renderer.Calls) > 0), "the null device never rendered");

        Assert.Equal(2, renderer.Channels);
        Assert.True(renderer.Samples % 2 == 0, "a render buffer was not a whole number of frames");
        Assert.Null(device.Fault);
    }

    [Fact]
    public void ThrowingRendererLatchesTheFaultAndIsNotCalledAgain()
    {
        using var context = TestAudio.NullContext();
        var renderer = new ThrowingRenderer();
        using var device = AudioDevice.Create(context, default, renderer);

        device.Start();
        Assert.True(TestAudio.WaitUntil(() => device.Fault is not null), "the fault was never latched");
        var calls = Volatile.Read(ref renderer.Calls);
        Thread.Sleep(100); // several periods

        Assert.IsType<InvalidOperationException>(device.Fault);
        Assert.Equal(calls, Volatile.Read(ref renderer.Calls));
        Assert.True(device.IsStarted, "a renderer fault must not stop the device");
    }

    [Fact]
    public void StartAndStopDeliverNotifications()
    {
        using var context = TestAudio.NullContext();
        var renderer = new CountingRenderer();
        using var device = AudioDevice.Create(context, default, renderer);

        device.Start();
        device.Stop();

        Assert.True(TestAudio.WaitUntil(() => renderer.Notifications.Contains(AudioDeviceNotification.Stopped)));
        Assert.Contains(AudioDeviceNotification.Started, renderer.Notifications);
    }

    [Fact]
    public void RenderPathAllocatesNothing()
    {
        using var context = TestAudio.NullContext();
        var renderer = new AllocationProbeRenderer();
        using var device = AudioDevice.Create(context, default, renderer);

        device.Start();
        Assert.True(TestAudio.WaitUntil(() => Volatile.Read(ref renderer.Done)), "the probe never finished");

        Assert.Equal(0, renderer.Allocated);
    }

    [Fact]
    public void DeviceCanPlayAPullEngine()
    {
        using var context = TestAudio.NullContext();
        using var engine = TestAudio.PullEngine();
        using var asset = TestAudio.ConstantAsset(TestAudio.EngineRate);
        using var sound = Sound.Create(engine, asset, new SoundDescription { Looping = true });
        sound.Play();

        var renderer = new EngineRenderer(engine);
        using var device = AudioDevice.Create(context, new AudioDeviceDescription { Channels = engine.Channels, SampleRate = engine.SampleRate }, renderer);
        device.Start();

        Assert.True(TestAudio.WaitUntil(() => engine.TimeInFrames > 4800), "the device never pulled the engine");
        Assert.True(Volatile.Read(ref renderer.Peak) > 0.1f, $"peak {renderer.Peak}");
        Assert.Null(device.Fault);

        device.Dispose(); // before the engine it reads
    }

    [Fact]
    public void DisposingTheContextDisposesItsDevices()
    {
        var context = TestAudio.NullContext();
        var device = AudioDevice.Create(context, default, new CountingRenderer());
        device.Start();

        context.Dispose();

        Assert.Throws<ObjectDisposedException>(() => device.IsStarted);
        device.Dispose(); // harmless
        context.Dispose(); // harmless
    }

    [Fact]
    public void DescriptionRejectsNegativeValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AudioDevice.Create(null, new AudioDeviceDescription { Channels = -1 }, new CountingRenderer()));
    }

    private sealed class CountingRenderer : IAudioRenderer
    {
        public int Calls;
        public int Channels;
        public int Samples;
        public readonly ConcurrentQueue<AudioDeviceNotification> Notifications = new();

        public void Render(Span<float> output, int channels)
        {
            Channels = channels;
            Samples = output.Length;
            Interlocked.Increment(ref Calls);
        }

        public void OnNotification(AudioDeviceNotification notification) => Notifications.Enqueue(notification);
    }

    private sealed class ThrowingRenderer : IAudioRenderer
    {
        public int Calls;

        public void Render(Span<float> output, int channels)
        {
            Interlocked.Increment(ref Calls);
            throw new InvalidOperationException("renderer bug");
        }
    }

    // Measures the audio thread's own allocations between two later calls, so
    // the wrapper's callback path is inside the window (first calls warm up
    // the JIT and the thread's runtime attachment).
    private sealed class AllocationProbeRenderer : IAudioRenderer
    {
        private int _calls;
        private long _start;
        public long Allocated = -1;
        public bool Done;

        public void Render(Span<float> output, int channels)
        {
            var call = ++_calls;
            if (call == 5)
            {
                _start = GC.GetAllocatedBytesForCurrentThread();
            }
            else if (call == 25)
            {
                Allocated = GC.GetAllocatedBytesForCurrentThread() - _start;
                Volatile.Write(ref Done, true);
            }
        }
    }

    private sealed class EngineRenderer(AudioEngine engine) : IAudioRenderer
    {
        public float Peak;

        public void Render(Span<float> output, int channels)
        {
            engine.Read(output);
            Volatile.Write(ref Peak, Math.Max(Peak, TestAudio.Peak(output)));
        }
    }
}

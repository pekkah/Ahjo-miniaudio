using System.Collections.Concurrent;

using Xunit;

namespace Ahjo.Miniaudio.Tests;

/// <summary>
/// An engine that opens its own device: its state (<see cref="AudioEngine.IsStarted"/>)
/// and notifications (<see cref="AudioEngineDescription.DeviceObserver"/>).
/// </summary>
/// <remarks>
/// The null backend never reroutes and never loses its device, so
/// <see cref="AudioDeviceNotification.Rerouted"/> and an unrequested
/// <see cref="AudioDeviceNotification.Stopped"/> cannot be produced here.
/// What these tests prove is the delivery path those use: the same callback,
/// handle and fault latch, driven by Start, Stop and Dispose.
/// </remarks>
public class EngineDeviceTests
{
    [Fact]
    public void ObserverSeesTheStartDuringCreateAndEachStopAndStart()
    {
        using var context = TestAudio.NullContext();
        var observer = new RecordingObserver();
        using var engine = AudioEngine.Create(new AudioEngineDescription { Context = context, DeviceObserver = observer });

        // The engine starts itself inside Create.
        Assert.True(TestAudio.WaitUntil(() => observer.Contains(AudioDeviceNotification.Started)));
        Assert.True(engine.IsStarted);

        engine.Stop();
        Assert.True(TestAudio.WaitUntil(() => observer.Contains(AudioDeviceNotification.Stopped)));
        Assert.False(engine.IsStarted);

        observer.Clear();
        engine.Start();
        Assert.True(TestAudio.WaitUntil(() => observer.Contains(AudioDeviceNotification.Started)));
        Assert.True(engine.IsStarted);
        Assert.Null(engine.Fault);
    }

    [Fact]
    public void NoAutoStartEngineIsStoppedUntilStarted()
    {
        using var context = TestAudio.NullContext();
        using var engine = AudioEngine.Create(new AudioEngineDescription { Context = context, NoAutoStart = true });

        Assert.False(engine.IsStarted);
        engine.Start();
        Assert.True(engine.IsStarted);
    }

    [Fact]
    public void DisposeDeliversStoppedWhileTheHandleIsStillValid()
    {
        var context = TestAudio.NullContext();
        var observer = new RecordingObserver();
        var engine = AudioEngine.Create(new AudioEngineDescription { Context = context, DeviceObserver = observer });
        Assert.True(TestAudio.WaitUntil(() => observer.Contains(AudioDeviceNotification.Started)));
        observer.Clear();

        engine.Dispose();

        Assert.True(observer.Contains(AudioDeviceNotification.Stopped), "uninit's Stopped notification was not delivered");
        context.Dispose();
    }

    [Fact]
    public void DisposingTheContextDeliversTheEnginesStopped()
    {
        var context = TestAudio.NullContext();
        var observer = new RecordingObserver();
        var engine = AudioEngine.Create(new AudioEngineDescription { Context = context, DeviceObserver = observer });
        Assert.True(TestAudio.WaitUntil(() => observer.Contains(AudioDeviceNotification.Started)));

        context.Dispose();

        Assert.True(observer.Contains(AudioDeviceNotification.Stopped));
        Assert.Throws<ObjectDisposedException>(() => engine.IsStarted);
    }

    [Fact]
    public void ThrowingObserverLatchesTheFaultAndTheEngineKeepsPlaying()
    {
        using var context = TestAudio.NullContext();
        var observer = new ThrowingObserver();
        using var engine = AudioEngine.Create(new AudioEngineDescription { Context = context, DeviceObserver = observer });
        using var asset = TestAudio.ConstantAsset(480);
        using var sound = Sound.Create(engine, asset, new SoundDescription { Looping = true });
        sound.Play();

        Assert.True(TestAudio.WaitUntil(() => engine.Fault is not null), "the observer's exception was not latched");
        engine.Stop();
        engine.Start();

        Assert.Equal(1, Volatile.Read(ref observer.Calls)); // not called again once faulted
        Assert.IsType<InvalidOperationException>(engine.Fault);
        var time = engine.TimeInFrames;
        Assert.True(TestAudio.WaitUntil(() => engine.TimeInFrames > time), "a faulted observer must not stop the mix");
    }

    [Fact]
    public void ObserverOnANoDeviceEngineIsRejected()
    {
        var e = Assert.Throws<ArgumentException>(() =>
            AudioEngine.Create(new AudioEngineDescription { NoDevice = true, DeviceObserver = new RecordingObserver() }));

        Assert.Equal("DeviceObserver", e.ParamName);
    }

    [Fact]
    public void NoDeviceEngineIsNeverStarted()
    {
        using var engine = TestAudio.PullEngine();

        Assert.False(engine.IsStarted);
    }

    [Fact]
    public void DeviceRendererNotificationFaultStopsRendering()
    {
        using var context = TestAudio.NullContext();
        var renderer = new NotificationThrowingRenderer();
        using var device = AudioDevice.Create(context, default, renderer);

        device.Start(); // Started throws inside the renderer's observer

        Assert.True(TestAudio.WaitUntil(() => device.Fault is not null));
        var calls = Volatile.Read(ref renderer.RenderCalls);
        Thread.Sleep(100);
        Assert.Equal(calls, Volatile.Read(ref renderer.RenderCalls));
    }

    private sealed class RecordingObserver : IAudioDeviceObserver
    {
        private readonly ConcurrentQueue<AudioDeviceNotification> _seen = new();

        public void OnNotification(AudioDeviceNotification notification) => _seen.Enqueue(notification);

        public bool Contains(AudioDeviceNotification notification) => _seen.Contains(notification);

        public void Clear() => _seen.Clear();
    }

    private sealed class ThrowingObserver : IAudioDeviceObserver
    {
        public int Calls;

        public void OnNotification(AudioDeviceNotification notification)
        {
            Interlocked.Increment(ref Calls);
            throw new InvalidOperationException("observer bug");
        }
    }

    private sealed class NotificationThrowingRenderer : IAudioRenderer
    {
        public int RenderCalls;

        public void Render(Span<float> output, int channels) => Interlocked.Increment(ref RenderCalls);

        public void OnNotification(AudioDeviceNotification notification) =>
            throw new InvalidOperationException("observer bug");
    }
}

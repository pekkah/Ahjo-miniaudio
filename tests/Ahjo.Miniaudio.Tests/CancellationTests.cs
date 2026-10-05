using Ahjo.Miniaudio.Internal;
using Ahjo.Miniaudio.Native;

using Xunit;

namespace Ahjo.Miniaudio.Tests;

/// <summary>
/// The factories' cancellation token. A backend that never answers (a wedged
/// PulseAudio server) cannot be staged on the null backend, so the abandon
/// path is driven through <see cref="AbandonableCall"/> with a managed call
/// that blocks until the test releases it.
/// </summary>
public class CancellationTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public void AnUncancellableTokenRunsTheCallInline()
    {
        var caller = Environment.CurrentManagedThreadId;
        var callThread = 0;

        var result = AbandonableCall.Run(
            () =>
            {
                callThread = Environment.CurrentManagedThreadId;
                return ma_result.MA_SUCCESS;
            },
            _ => Assert.Fail("nothing was abandoned"),
            CancellationToken.None);

        Assert.Equal(ma_result.MA_SUCCESS, result);
        Assert.Equal(caller, callThread);
    }

    [Fact]
    public void ACancellableTokenRunsTheCallOnItsOwnThread()
    {
        using var cts = new CancellationTokenSource();
        var caller = Environment.CurrentManagedThreadId;
        var callThread = 0;

        var result = AbandonableCall.Run(
            () =>
            {
                callThread = Environment.CurrentManagedThreadId;
                return ma_result.MA_DEVICE_NOT_INITIALIZED;
            },
            _ => Assert.Fail("nothing was abandoned"),
            cts.Token);

        Assert.Equal(ma_result.MA_DEVICE_NOT_INITIALIZED, result);
        Assert.NotEqual(caller, callThread);
    }

    [Fact]
    public void CancellingStopsTheWaitAndHandsTheLateResultToAbandon()
    {
        using var release = new ManualResetEventSlim();
        using var abandoned = new ManualResetEventSlim();
        var abandonCalls = 0;
        var lateResult = ma_result.MA_ERROR;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        Assert.Throws<OperationCanceledException>(() => AbandonableCall.Run(
            () =>
            {
                release.Wait(Patience, TestContext.Current.CancellationToken);
                return ma_result.MA_SUCCESS;
            },
            late =>
            {
                lateResult = late;
                Interlocked.Increment(ref abandonCalls);
                abandoned.Set();
            },
            cts.Token));

        // The caller is back while the call is still blocked.
        Assert.Equal(0, Volatile.Read(ref abandonCalls));

        release.Set();
        Assert.True(abandoned.Wait(Patience, TestContext.Current.CancellationToken), "the abandoned call was never cleaned up");
        Assert.Equal(1, Volatile.Read(ref abandonCalls));
        Assert.Equal(ma_result.MA_SUCCESS, lateResult);
    }

    [Fact]
    public void AnAlreadyCancelledTokenFreesWithoutRunningTheCall()
    {
        var abandonedWith = (ma_result?)null;

        Assert.Throws<OperationCanceledException>(() => AbandonableCall.Run(
            () =>
            {
                Assert.Fail("the call ran");
                return ma_result.MA_SUCCESS;
            },
            late => abandonedWith = late,
            new CancellationToken(canceled: true)));

        Assert.Equal(ma_result.MA_CANCELLED, abandonedWith);
    }

    [Fact]
    public void AnExceptionInTheCallReachesTheCaller()
    {
        using var cts = new CancellationTokenSource();

        var e = Assert.Throws<InvalidOperationException>(() => AbandonableCall.Run(
            () => throw new InvalidOperationException("boom"),
            _ => Assert.Fail("nothing was abandoned"),
            cts.Token));

        Assert.Equal("boom", e.Message);
    }

    [Fact]
    public void DisposingAContextWithAnAbandonedInitDefersTheNativeRelease()
    {
        var context = TestAudio.NullContext();
        using var release = new ManualResetEventSlim();
        using var abandoned = new ManualResetEventSlim();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        Assert.Throws<OperationCanceledException>(() => AbandonableCall.Run(
            () =>
            {
                release.Wait(Patience, TestContext.Current.CancellationToken);
                return ma_result.MA_ERROR;
            },
            _ => abandoned.Set(),
            cts.Token,
            outer: context.Calls));

        // Disposed for the caller, but the init is still using the native
        // context, so it stays allocated.
        context.Dispose();
        Assert.Throws<ObjectDisposedException>(() => context.Backend);
        Assert.True(context.IsNativeAlive);

        release.Set();
        Assert.True(abandoned.Wait(Patience, TestContext.Current.CancellationToken), "the abandoned call was never cleaned up");
        Assert.True(SpinWait.SpinUntil(() => !context.IsNativeAlive, Patience), "the context was never released");
    }

    [Fact]
    public void AContextDisposedWithNoInitInFlightReleasesAtOnce()
    {
        var context = TestAudio.NullContext();

        context.Dispose();

        Assert.False(context.IsNativeAlive);
    }

    [Fact]
    public void ReleaseRunsAtOnceWithNothingInFlight()
    {
        var calls = new InFlightCalls(typeof(CancellationTests));
        var released = 0;

        calls.Release(() => released++);

        Assert.Equal(1, released);
        Assert.True(calls.IsReleased);
    }

    [Fact]
    public void ReleaseWaitsForTheLastCallInFlight()
    {
        var calls = new InFlightCalls(typeof(CancellationTests));
        var released = 0;
        calls.Begin();
        calls.Begin();

        calls.Release(() => released++);
        Assert.Equal(0, released);

        calls.End();
        Assert.Equal(0, released);
        Assert.False(calls.IsReleased);

        calls.End();
        Assert.Equal(1, released);
        Assert.True(calls.IsReleased);
    }

    [Fact]
    public void NoCallBeginsAfterRelease()
    {
        var calls = new InFlightCalls(typeof(CancellationTests));
        calls.Release(() => { });

        Assert.Throws<ObjectDisposedException>(calls.Begin);
    }

    [Fact]
    public void AnOuterOwnerOutlivesTheInnerOne()
    {
        // A device's deferred uninit needs its context: inner must end (and
        // release) before outer does.
        var inner = new InFlightCalls(typeof(AudioDevice));
        var outer = new InFlightCalls(typeof(AudioContext));
        var order = new List<string>();
        using var release = new ManualResetEventSlim();
        using var abandoned = new ManualResetEventSlim();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        Assert.Throws<OperationCanceledException>(() => AbandonableCall.Run(
            () =>
            {
                release.Wait(Patience, TestContext.Current.CancellationToken);
                return ma_result.MA_SUCCESS;
            },
            _ => { },
            cts.Token,
            inner,
            outer));

        inner.Release(() => order.Add("inner"));
        outer.Release(() =>
        {
            order.Add("outer");
            abandoned.Set();
        });
        Assert.Empty(order);

        release.Set();
        Assert.True(abandoned.Wait(Patience, TestContext.Current.CancellationToken), "the owners were never released");
        Assert.Equal(["inner", "outer"], order);
    }

    [Fact]
    public void TheFactoriesHonourAnAlreadyCancelledToken()
    {
        var cancelled = new CancellationToken(canceled: true);
        using var context = TestAudio.NullContext();

        Assert.Throws<OperationCanceledException>(() =>
            AudioContext.Create(new AudioContextDescription { Backend = AudioBackend.Null }, cancelled));
        Assert.Throws<OperationCanceledException>(() =>
            AudioDevice.Create(context, default, new SilentRenderer(), cancelled));
        Assert.Throws<OperationCanceledException>(() =>
            AudioEngine.Create(new AudioEngineDescription { Context = context }, cancelled));
    }

    [Fact]
    public void TheFactoriesSucceedWithALiveToken()
    {
        using var cts = new CancellationTokenSource(Patience);

        using var context = AudioContext.Create(new AudioContextDescription { Backend = AudioBackend.Null }, cts.Token);
        using var device = AudioDevice.Create(context, default, new SilentRenderer(), cts.Token);
        using var engine = AudioEngine.Create(new AudioEngineDescription { Context = context, NoAutoStart = true }, cts.Token);

        Assert.Equal(AudioBackend.Null, context.Backend);
        Assert.True(device.Channels > 0);
        Assert.True(engine.Channels > 0);
    }

    private sealed class SilentRenderer : IAudioRenderer
    {
        public void Render(Span<float> output, int channels) => output.Clear();
    }
}

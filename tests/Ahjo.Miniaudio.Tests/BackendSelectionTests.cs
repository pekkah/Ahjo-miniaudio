using Ahjo.Miniaudio.Internal;
using Ahjo.Miniaudio.Native;

using Xunit;

namespace Ahjo.Miniaudio.Tests;

/// <summary>
/// miniaudio ends its default backend list with the null backend, so "no
/// audio" used to be a silent success. Here Null is tried only when the
/// caller allows it. Whether a real backend opens depends on the machine, so
/// these tests pin the rules, and assert only what holds on every CI lane.
/// </summary>
public unsafe class BackendSelectionTests
{
    [Fact]
    public void TheDefaultListIsEveryRealBackendInPriorityOrder()
    {
        var backends = AudioContext.BackendsToTry(null, allowNull: false);

        Assert.DoesNotContain(ma_backend.ma_backend_null, backends);
        Assert.Equal(ma_backend.ma_backend_wasapi, backends[0]);
        Assert.Equal((int)ma_backend.ma_backend_null, backends.Length);
        Assert.Equal(backends.Order(), backends);
    }

    [Fact]
    public void AllowingNullAppendsItLast()
    {
        var backends = AudioContext.BackendsToTry(null, allowNull: true);

        Assert.Equal(ma_backend.ma_backend_null, backends[^1]);
        Assert.Equal(AudioContext.BackendsToTry(null, allowNull: false), backends[..^1]);
    }

    [Fact]
    public void ARequestedBackendIsTheWholeList()
    {
        Assert.Equal([ma_backend.ma_backend_null], AudioContext.BackendsToTry(AudioBackend.Null, allowNull: false));
        Assert.Equal([ma_backend.ma_backend_pulseaudio], AudioContext.BackendsToTry(AudioBackend.PulseAudio, allowNull: true));
    }

    [Fact]
    public void FallbackReportsNoBackendWhenNothingInitializes()
    {
        var context = (ma_context*)NativeBlock.Alloc(Ma.ahjo_ma_sizeof_ma_context());
        try
        {
            Assert.Equal(ma_result.MA_NO_BACKEND, BackendFallback.Open([], context, _ => ma_result.MA_SUCCESS));
        }
        finally
        {
            NativeBlock.Free(context);
        }
    }

    [Fact]
    public void FallbackReportsTheLastOpenFailure()
    {
        var context = (ma_context*)NativeBlock.Alloc(Ma.ahjo_ma_sizeof_ma_context());
        try
        {
            // The null context initializes; the open on it is what failed,
            // which is the more useful error.
            var result = BackendFallback.Open([ma_backend.ma_backend_null], context, _ => ma_result.MA_NO_DEVICE);
            Assert.Equal(ma_result.MA_NO_DEVICE, result);
        }
        finally
        {
            NativeBlock.Free(context);
        }
    }

    [Fact]
    public void FallbackLeavesTheWinningContextInitialized()
    {
        var context = (ma_context*)NativeBlock.Alloc(Ma.ahjo_ma_sizeof_ma_context());
        try
        {
            var opened = BackendFallback.Open([ma_backend.ma_backend_null], context, _ => ma_result.MA_SUCCESS);

            Assert.Equal(ma_result.MA_SUCCESS, opened);
            Assert.Equal(ma_backend.ma_backend_null, Ma.ahjo_ma_context_get_backend(context));
            Ma.ma_context_uninit(context);
        }
        finally
        {
            NativeBlock.Free(context);
        }
    }

    [Fact]
    public void WithNullAllowedAContextlessEngineAndDeviceAlwaysOpen()
    {
        // A real backend locally, Null on a headless runner: either way it
        // opens and says which. Bounded in case a headless backend stalls.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        using var engine = AudioEngine.Create(new AudioEngineDescription { AllowNullBackend = true, NoAutoStart = true }, timeout.Token);
        using var device = AudioDevice.Create(null, new AudioDeviceDescription { AllowNullBackend = true }, new SilentRenderer(), timeout.Token);

        Assert.NotNull(engine.Backend);
        Assert.True(Enum.IsDefined(device.Backend));
    }

    [Fact]
    public void WithNullAllowedAContextlessContextAlwaysOpens()
    {
        using var context = AudioContext.Create(new AudioContextDescription { AllowNullBackend = true }, TestContext.Current.CancellationToken);

        Assert.True(Enum.IsDefined(context.Backend));
    }

    [Fact]
    public void ADeviceOnAContextReportsThatContextsBackend()
    {
        using var context = TestAudio.NullContext();
        using var device = AudioDevice.Create(context, default, new SilentRenderer(), TestContext.Current.CancellationToken);
        using var engine = AudioEngine.Create(new AudioEngineDescription { Context = context, NoAutoStart = true }, TestContext.Current.CancellationToken);

        Assert.Equal(AudioBackend.Null, device.Backend);
        Assert.Equal(AudioBackend.Null, engine.Backend);
    }

    [Fact]
    public void ANoDeviceEngineHasNoBackend()
    {
        using var engine = TestAudio.PullEngine();

        Assert.Null(engine.Backend);
    }

    private sealed class SilentRenderer : IAudioRenderer
    {
        public void Render(Span<float> output, int channels) => output.Clear();
    }
}

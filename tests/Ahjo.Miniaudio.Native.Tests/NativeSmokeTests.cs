using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Xunit;

namespace Ahjo.Miniaudio.Native.Tests;

/// <summary>
/// Executes the native library this package ships. A binding that compiles
/// proves nothing about a binary nobody has loaded; these tests run the real
/// thing on the RID it was built for.
///
/// Everything here uses miniaudio's null backend, which simulates a device on
/// a timer thread. CI runners have no audio hardware, and the null backend is
/// what lets the context/device lifecycle — including the data callback on
/// miniaudio's own thread — run honestly without one.
/// </summary>
public unsafe class NativeSmokeTests
{
    [Fact]
    public void VersionMatchesPinnedRelease()
    {
        // Proves both that the library loads and that it was built from the
        // header the bindings were generated from. A stale staged binary
        // after a MiniaudioVersion bump fails here, not in some layout bug.
        var pinned = Version.Parse(typeof(NativeSmokeTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "MiniaudioVersion").Value!);

        uint major, minor, revision;
        Ma.ma_version(&major, &minor, &revision);

        Assert.Equal(pinned, new Version((int)major, (int)minor, (int)revision));
        Assert.Equal(pinned.ToString(), Marshal.PtrToStringUTF8((nint)Ma.ma_version_string()));
    }

    [Fact]
    public void NullBackendContextEnumeratesItsDevice()
    {
        var context = Alloc<ma_context>();
        try
        {
            InitNullContext(context);
            try
            {
                ma_device_info* playback;
                ma_device_info* capture;
                uint playbackCount, captureCount;
                Assert.Equal(ma_result.MA_SUCCESS,
                    Ma.ma_context_get_devices(context, &playback, &playbackCount, &capture, &captureCount));

                Assert.Equal(ma_backend.ma_backend_null, context->backend);
                Assert.True(playbackCount >= 1, $"null backend reported {playbackCount} playback devices");
            }
            finally
            {
                Ma.ma_context_uninit(context);
            }
        }
        finally
        {
            NativeMemory.AlignedFree(context);
        }
    }

    [Fact]
    public void NullBackendDeviceInvokesDataCallback()
    {
        var context = Alloc<ma_context>();
        var device = Alloc<ma_device>();
        var framesRequested = (long*)NativeMemory.AllocZeroed((nuint)sizeof(long));
        try
        {
            InitNullContext(context);
            try
            {
                var config = Ma.ma_device_config_init(ma_device_type.ma_device_type_playback);
                config.playback.format = ma_format.ma_format_f32;
                config.playback.channels = 2;
                config.sampleRate = 48000;
                config.dataCallback = &OnData;
                config.pUserData = framesRequested;

                Assert.Equal(ma_result.MA_SUCCESS, Ma.ma_device_init(context, &config, device));
                try
                {
                    Assert.Equal(ma_result.MA_SUCCESS, Ma.ma_device_start(device));

                    var clock = Stopwatch.StartNew();
                    while (Volatile.Read(ref *framesRequested) == 0 && clock.Elapsed < TimeSpan.FromSeconds(5))
                    {
                        Thread.Sleep(10);
                    }

                    Assert.True(Volatile.Read(ref *framesRequested) > 0,
                        "the null backend never called the data callback within 5 s");
                }
                finally
                {
                    Ma.ma_device_uninit(device);
                }
            }
            finally
            {
                Ma.ma_context_uninit(context);
            }
        }
        finally
        {
            NativeMemory.Free(framesRequested);
            NativeMemory.AlignedFree(device);
            NativeMemory.AlignedFree(context);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnData(ma_device* device, void* output, void* input, uint frameCount)
    {
        Interlocked.Add(ref *(long*)device->pUserData, frameCount);
    }

    private static void InitNullContext(ma_context* context)
    {
        var backend = ma_backend.ma_backend_null;
        var config = Ma.ma_context_config_init();
        Assert.Equal(ma_result.MA_SUCCESS, Ma.ma_context_init(&backend, 1, &config, context));
    }

    // 64 bytes covers every alignment miniaudio declares (MA_ATOMIC and
    // MA_SIMD_ALIGNMENT top out at 32).
    private static T* Alloc<T>() where T : unmanaged
    {
        var p = (T*)NativeMemory.AlignedAlloc((nuint)sizeof(T), 64);
        NativeMemory.Clear(p, (nuint)sizeof(T));
        return p;
    }
}

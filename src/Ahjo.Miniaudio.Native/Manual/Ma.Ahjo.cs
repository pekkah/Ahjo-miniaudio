using System.Runtime.InteropServices;

namespace Ahjo.Miniaudio.Native;

// The ahjo_ma_* exports of native/miniaudio/src/ahjo_miniaudio.c that
// consumers need: the native size of each opaque type (Opaque.cs), and
// accessors for the opaque types' fields that miniaudio has no getter for.
// Hand-written because the generator parses miniaudio.h only.
public static unsafe partial class Ma
{
    [DllImport("ahjo_miniaudio", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("size_t")]
    public static extern nuint ahjo_ma_sizeof_ma_context();

    [DllImport("ahjo_miniaudio", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("size_t")]
    public static extern nuint ahjo_ma_sizeof_ma_device();

    [DllImport("ahjo_miniaudio", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("size_t")]
    public static extern nuint ahjo_ma_sizeof_ma_resource_manager();

    [DllImport("ahjo_miniaudio", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("size_t")]
    public static extern nuint ahjo_ma_sizeof_ma_log();

    [DllImport("ahjo_miniaudio", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("size_t")]
    public static extern nuint ahjo_ma_sizeof_ma_fence();

    [DllImport("ahjo_miniaudio", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("size_t")]
    public static extern nuint ahjo_ma_sizeof_ma_async_notification_event();

    [DllImport("ahjo_miniaudio", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("size_t")]
    public static extern nuint ahjo_ma_sizeof_ma_job_queue();

    [DllImport("ahjo_miniaudio", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("size_t")]
    public static extern nuint ahjo_ma_sizeof_ma_device_job_thread();

    [DllImport("ahjo_miniaudio", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("size_t")]
    public static extern nuint ahjo_ma_sizeof_ma_mutex();

    [DllImport("ahjo_miniaudio", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("size_t")]
    public static extern nuint ahjo_ma_sizeof_ma_event();

    [DllImport("ahjo_miniaudio", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("size_t")]
    public static extern nuint ahjo_ma_sizeof_ma_semaphore();

    [DllImport("ahjo_miniaudio", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("size_t")]
    public static extern nuint ahjo_ma_sizeof_ma_thread();

    /// <summary><c>pContext-&gt;backend</c>.</summary>
    [DllImport("ahjo_miniaudio", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern ma_backend ahjo_ma_context_get_backend([NativeTypeName("const ma_context *")] ma_context* pContext);

    /// <summary><c>pDevice-&gt;pUserData</c>. A plain load, safe to call from the data callback.</summary>
    [DllImport("ahjo_miniaudio", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [SuppressGCTransition]
    public static extern void* ahjo_ma_device_get_user_data([NativeTypeName("const ma_device *")] ma_device* pDevice);

    /// <summary><c>pDevice-&gt;playback.channels</c>: the channel count the device opened with.</summary>
    [DllImport("ahjo_miniaudio", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("ma_uint32")]
    public static extern uint ahjo_ma_device_get_playback_channels([NativeTypeName("const ma_device *")] ma_device* pDevice);

    /// <summary><c>pDevice-&gt;sampleRate</c>: the sample rate the device opened with.</summary>
    [DllImport("ahjo_miniaudio", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [return: NativeTypeName("ma_uint32")]
    public static extern uint ahjo_ma_device_get_sample_rate([NativeTypeName("const ma_device *")] ma_device* pDevice);
}

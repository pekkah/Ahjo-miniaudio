using System.Runtime.InteropServices;

using Xunit;

namespace Ahjo.Miniaudio.Native.Tests;

/// <summary>
/// Compares the generated C# structs against the compiler's own sizeof, read
/// from the <c>ahjo_ma_sizeof_*</c> exports in native/miniaudio/src/ahjo_miniaudio.c.
///
/// The bindings are generated for x86_64-pc-windows-msvc, and miniaudio's
/// runtime-state structs are platform-specific (per-backend members, pthread
/// types on POSIX). This suite is what proves the bindings describe the
/// binary on the RID it runs on — and what will fail first on any RID they
/// do not describe. Never "fix" a failure here by editing the expected
/// size: a mismatch is a real layout bug.
/// </summary>
public unsafe class LayoutTests
{
    public static TheoryData<string, int> Types => new()
    {
        { "ma_context", sizeof(ma_context) },
        { "ma_context_config", sizeof(ma_context_config) },
        { "ma_device_id", sizeof(ma_device_id) },
        { "ma_device", sizeof(ma_device) },
        { "ma_device_config", sizeof(ma_device_config) },
        { "ma_device_info", sizeof(ma_device_info) },
        { "ma_device_notification", sizeof(ma_device_notification) },
        { "ma_decoder", sizeof(ma_decoder) },
        { "ma_decoder_config", sizeof(ma_decoder_config) },
        { "ma_audio_buffer_ref", sizeof(ma_audio_buffer_ref) },
        { "ma_encoder", sizeof(ma_encoder) },
        { "ma_encoder_config", sizeof(ma_encoder_config) },
        { "ma_engine", sizeof(ma_engine) },
        { "ma_engine_config", sizeof(ma_engine_config) },
        { "ma_sound", sizeof(ma_sound) },
        { "ma_sound_config", sizeof(ma_sound_config) },
        // ma_sound_group is a typedef of ma_sound; measured separately on the C side.
        { "ma_sound_group", sizeof(ma_sound) },
        { "ma_resource_manager", sizeof(ma_resource_manager) },
        { "ma_resource_manager_config", sizeof(ma_resource_manager_config) },
        { "ma_node_graph", sizeof(ma_node_graph) },
        // Returned by value from the listener/sound position getters.
        { "ma_vec3f", sizeof(ma_vec3f) },
        // On Windows these four are typedefs of ma_handle (void*), so the
        // generator erases them to void* and there is no C# type to measure.
        // On POSIX they are pthread structs; that difference is exactly what
        // keeps Linux unshipped (CLAUDE.md, "Platform layouts").
        { "ma_mutex", sizeof(void*) },
        { "ma_event", sizeof(void*) },
        { "ma_semaphore", sizeof(void*) },
        { "ma_thread", sizeof(void*) },
    };

    [Theory]
    [MemberData(nameof(Types))]
    public void ManagedSizeMatchesNative(string type, int managedSize)
    {
        var library = NativeLibrary.Load("ahjo_miniaudio", typeof(Ma).Assembly, null);
        var sizeOf = (delegate* unmanaged[Cdecl]<nuint>)NativeLibrary.GetExport(library, $"ahjo_ma_sizeof_{type}");

        Assert.Equal((nuint)managedSize, sizeOf());
    }
}

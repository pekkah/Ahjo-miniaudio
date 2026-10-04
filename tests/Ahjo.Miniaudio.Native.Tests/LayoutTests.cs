using System.Reflection;
using System.Runtime.InteropServices;

using Xunit;

namespace Ahjo.Miniaudio.Native.Tests;

/// <summary>
/// Compares the generated C# structs against the compiler's own sizeof and
/// offsetof, read from the <c>ahjo_ma_sizeof_*</c> / <c>ahjo_ma_offsetof_*</c>
/// exports in native/miniaudio/src/ahjo_miniaudio.c.
///
/// One set of bindings serves every RID: the platform-dependent runtime
/// state (ma_context, ma_device, the sync types, ...) is excluded from
/// generation and declared opaque, and everything left is meant to be laid
/// out the same on every target. This suite is what proves that on the RID it
/// runs on. Never "fix" a failure here by editing the expected value: a
/// mismatch is a real layout bug.
/// </summary>
public unsafe class LayoutTests
{
    public static TheoryData<string, int> Types => new()
    {
        { "ma_context_config", sizeof(ma_context_config) },
        { "ma_device_id", sizeof(ma_device_id) },
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
        { "ma_resource_manager_config", sizeof(ma_resource_manager_config) },
        { "ma_node_graph", sizeof(ma_node_graph) },
        // Returned by value from the listener/sound position getters.
        { "ma_vec3f", sizeof(ma_vec3f) },
    };

    // The fields the wrapper reads or writes directly. A field can move
    // inside a struct whose size stays the same, which Types cannot see.
    public static TheoryData<string, int> Fields
    {
        get
        {
            ma_engine engine;
            ma_device_notification notification;
            ma_device_info info;
            ma_audio_buffer_ref buffer;
            return new()
            {
                { "ma_engine_pProcessUserData", Offset(&engine, &engine.pProcessUserData) },
                { "ma_device_notification_pDevice", Offset(&notification, &notification.pDevice) },
                { "ma_device_notification_type", Offset(&notification, &notification.type) },
                { "ma_device_info_id", Offset(&info, &info.id) },
                { "ma_device_info_name", Offset(&info, &info.name) },
                { "ma_device_info_isDefault", Offset(&info, &info.isDefault) },
                { "ma_audio_buffer_ref_sampleRate", Offset(&buffer, &buffer.sampleRate) },
            };
        }
    }

    // Declared in Manual/Opaque.cs; their native size varies by platform.
    public static TheoryData<string> OpaqueTypes => new()
    {
        "ma_context",
        "ma_device",
        "ma_resource_manager",
        "ma_log",
        "ma_fence",
        "ma_async_notification_event",
        "ma_job_queue",
        "ma_device_job_thread",
        "ma_mutex",
        "ma_event",
        "ma_semaphore",
        "ma_thread",
    };

    [Theory]
    [MemberData(nameof(Types))]
    public void ManagedSizeMatchesNative(string type, int managedSize)
    {
        Assert.Equal((nuint)managedSize, Export($"ahjo_ma_sizeof_{type}")());
    }

    [Theory]
    [MemberData(nameof(Fields))]
    public void ManagedOffsetMatchesNative(string field, int managedOffset)
    {
        Assert.Equal((nuint)managedOffset, Export($"ahjo_ma_offsetof_{field}")());
    }

    [Theory]
    [MemberData(nameof(OpaqueTypes))]
    public void OpaqueTypeIsAllocatableAtItsNativeSize(string type)
    {
        // The managed declaration is empty, so its sizeof says nothing; the
        // Manual/ import is how consumers allocate it, and must be the
        // compiler's number.
        var import = typeof(Ma).GetMethod($"ahjo_ma_sizeof_{type}", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(import);
        Assert.NotNull(typeof(Ma).Assembly.GetType($"Ahjo.Miniaudio.Native.{type}"));

        var native = Export($"ahjo_ma_sizeof_{type}")();
        Assert.Equal(native, (nuint)import.Invoke(null, null)!);
        Assert.True(native >= (nuint)sizeof(void*), $"{type} reports {native} bytes");
    }

    [Fact]
    public void NoGeneratedStructEmbedsAnOpaqueType()
    {
        // An opaque type held by value would make its owner's sizeof wrong on
        // every platform but the one it happens to match — and Types would
        // only catch it if the owner is listed there. Pointers are fine.
        var opaque = OpaqueTypes.Select(row => typeof(Ma).Assembly.GetType($"Ahjo.Miniaudio.Native.{row.Data}")!).ToHashSet();
        var embeddings =
            from type in typeof(Ma).Assembly.GetTypes()
            where type.IsValueType
            from field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            where opaque.Contains(field.FieldType)
            select $"{type.FullName}.{field.Name}";

        Assert.Empty(embeddings);
    }

    private static int Offset(void* owner, void* field) => (int)((byte*)field - (byte*)owner);

    private static delegate* unmanaged[Cdecl]<nuint> Export(string name)
    {
        var library = NativeLibrary.Load("ahjo_miniaudio", typeof(Ma).Assembly, null);
        return (delegate* unmanaged[Cdecl]<nuint>)NativeLibrary.GetExport(library, name);
    }
}

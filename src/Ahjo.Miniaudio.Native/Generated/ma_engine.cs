using System.Runtime.CompilerServices;

namespace Ahjo.Miniaudio.Native;

public unsafe partial struct ma_engine
{
    public ma_node_graph nodeGraph;

    public ma_resource_manager* pResourceManager;

    public ma_device* pDevice;

    public ma_log* pLog;

    [NativeTypeName("ma_uint32")]
    public uint sampleRate;

    [NativeTypeName("ma_uint32")]
    public uint listenerCount;

    [NativeTypeName("ma_spatializer_listener[4]")]
    public _listeners_e__FixedBuffer listeners;

    public ma_allocation_callbacks allocationCallbacks;

    [NativeTypeName("ma_bool8")]
    public byte ownsResourceManager;

    [NativeTypeName("ma_bool8")]
    public byte ownsDevice;

    [NativeTypeName("ma_spinlock")]
    public uint inlinedSoundLock;

    public ma_sound_inlined* pInlinedSoundHead;

    [NativeTypeName("ma_uint32")]
    public uint inlinedSoundCount;

    [NativeTypeName("ma_uint32")]
    public uint gainSmoothTimeInFrames;

    [NativeTypeName("ma_uint32")]
    public uint defaultVolumeSmoothTimeInPCMFrames;

    public ma_mono_expansion_mode monoExpansionMode;

    [NativeTypeName("ma_engine_process_proc")]
    public delegate* unmanaged[Cdecl]<void*, float*, ulong, void> onProcess;

    public void* pProcessUserData;

    public ma_resampler_config pitchResamplingConfig;

    [InlineArray(4)]
    public partial struct _listeners_e__FixedBuffer
    {
        public ma_spatializer_listener e0;
    }
}

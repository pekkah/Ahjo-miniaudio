using System.Runtime.CompilerServices;

namespace Ahjo.Miniaudio.Native;

public unsafe partial struct ma_resource_manager_data_stream
{
    public ma_data_source_base ds;

    public ma_resource_manager* pResourceManager;

    [NativeTypeName("ma_uint32")]
    public uint flags;

    public ma_decoder decoder;

    [NativeTypeName("ma_bool32")]
    public uint isDecoderInitialized;

    [NativeTypeName("ma_uint64")]
    public ulong totalLengthInPCMFrames;

    [NativeTypeName("ma_uint32")]
    public uint relativeCursor;

    [NativeTypeName("ma_uint64")]
    public ulong absoluteCursor;

    [NativeTypeName("ma_uint32")]
    public uint currentPageIndex;

    [NativeTypeName("ma_uint32")]
    public uint executionCounter;

    [NativeTypeName("ma_uint32")]
    public uint executionPointer;

    [NativeTypeName("ma_bool32")]
    public uint isLooping;

    public void* pPageData;

    [NativeTypeName("ma_uint32[2]")]
    public _pageFrameCount_e__FixedBuffer pageFrameCount;

    public ma_result result;

    [NativeTypeName("ma_bool32")]
    public uint isDecoderAtEnd;

    [NativeTypeName("ma_bool32[2]")]
    public _isPageValid_e__FixedBuffer isPageValid;

    [NativeTypeName("ma_bool32")]
    public uint seekCounter;

    [InlineArray(2)]
    public partial struct _pageFrameCount_e__FixedBuffer
    {
        public uint e0;
    }

    [InlineArray(2)]
    public partial struct _isPageValid_e__FixedBuffer
    {
        public uint e0;
    }
}
